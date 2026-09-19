using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;
using YtpdWeb.Api.Data;
using YtpdWeb.Api.Hubs;
using YtpdWeb.Api.Models;
using TagLibFile = TagLib.File;

namespace YtpdWeb.Api.Services;

// Background consumer that mirrors what the original WPF app's DownloadPage did
// interactively (resolve streams -> download -> ffmpeg mux/convert -> tag),
// just running server-side against a queue instead of UI event handlers.
public class DownloadWorker(
    DownloadQueue queue,
    IServiceScopeFactory scopeFactory,
    IHubContext<ProgressHub> hub,
    IOptions<StorageOptions> storageOptions,
    IOptions<FfmpegOptions> ffmpegOptions,
    ILogger<DownloadWorker> logger
) : BackgroundService
{
    private const int Concurrency = 2;
    private readonly StorageOptions _storage = storageOptions.Value;
    private readonly string _ffmpegPath = ffmpegOptions.Value.Path;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(_storage.TempPath);
        Directory.CreateDirectory(_storage.DownloadsPath);

        var consumers = Enumerable.Range(0, Concurrency)
            .Select(_ => ConsumeLoopAsync(stoppingToken));

        await Task.WhenAll(consumers);
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        await foreach (var itemId in queue.ReadAllAsync(ct))
        {
            try
            {
                await ProcessItemAsync(itemId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled failure processing job item {ItemId}", itemId);
            }
        }
    }

    private async Task ProcessItemAsync(Guid itemId, CancellationToken appCt)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var item = await db.JobItems.Include(i => i.Job).FirstOrDefaultAsync(i => i.Id == itemId, appCt);
        if (item is null) return;

        // Cancelled while it was still sitting in the queue (never started) -
        // nothing to tear down, just leave the status as-is.
        if (item.Status == JobItemStatus.Cancelled) return;

        var tempDir = Path.Combine(_storage.TempPath, item.Id.ToString());
        Directory.CreateDirectory(tempDir);

        using var itemCts = queue.BeginTracking(item.Id, appCt);
        var ct = itemCts.Token;

        try
        {
            var youtube = new YoutubeClient();
            await SetStatusAsync(db, item, JobItemStatus.Resolving, 0, ct);

            var manifest = await youtube.Videos.Streams.GetManifestAsync(item.VideoId, ct);
            var format = item.Job.Format;
            var isVideoFormat = format is DownloadFormat.Mp4 or DownloadFormat.Mkv or DownloadFormat.Webm;

            var finalDir = Path.Combine(_storage.DownloadsPath, item.JobId.ToString());
            Directory.CreateDirectory(finalDir);
            var extension = format.ToString().ToLowerInvariant();
            var finalPath = Path.Combine(finalDir, $"{SanitizeFileName(item.Title)}.{extension}");

            if (isVideoFormat)
                await ProcessVideoAsync(youtube, manifest, item, tempDir, finalPath, db, ct);
            else
                await ProcessAudioAsync(youtube, manifest, item, tempDir, finalPath, db, ct);

            item.Status = JobItemStatus.Completed;
            item.Progress = 1;
            item.OutputFileName = Path.GetFileName(finalPath);
            item.ErrorMessage = null;
            await db.SaveChangesAsync(ct);
            await BroadcastAsync(item, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Use None below: `ct` is the (now-cancelled) per-item token, and
            // we still need this write to actually go through.
            item.Status = JobItemStatus.Cancelled;
            item.ErrorMessage = null;
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastAsync(item, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job item {ItemId} failed", item.Id);
            item.Status = JobItemStatus.Failed;
            item.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            await BroadcastAsync(item, CancellationToken.None);
        }
        finally
        {
            queue.EndTracking(item.Id);
            try { Directory.Delete(tempDir, true); } catch { /* best effort */ }
        }
    }

    private async Task ProcessVideoAsync(
        YoutubeClient youtube, StreamManifest manifest, DownloadJobItem item,
        string tempDir, string finalPath, AppDbContext db, CancellationToken ct)
    {
        var preferredContainer = item.Job.Format == DownloadFormat.Webm ? Container.WebM : Container.Mp4;

        var videoStream = QualitySelector.PickVideoStream(manifest, item.Job.Quality, preferredContainer)
            ?? throw new InvalidOperationException("No suitable video stream was found for this video.");
        var audioStream = QualitySelector.PickAudioStream(manifest, preferredContainer)
            ?? throw new InvalidOperationException("No suitable audio stream was found for this video.");

        await SetStatusAsync(db, item, JobItemStatus.Downloading, 0, ct);

        var videoTemp = Path.Combine(tempDir, $"video.{videoStream.Container.Name}");
        await DownloadWithProgressAsync(youtube, videoStream, videoTemp, 0.0, 0.5, db, item, ct);

        var audioTemp = Path.Combine(tempDir, $"audio.{audioStream.Container.Name}");
        await DownloadWithProgressAsync(youtube, audioStream, audioTemp, 0.5, 0.8, db, item, ct);

        await SetStatusAsync(db, item, JobItemStatus.Converting, 0.8, ct);
        await RunFfmpegAsync($"-i \"{videoTemp}\" -i \"{audioTemp}\" -y -c copy \"{finalPath}\"", ct);
    }

    private async Task ProcessAudioAsync(
        YoutubeClient youtube, StreamManifest manifest, DownloadJobItem item,
        string tempDir, string finalPath, AppDbContext db, CancellationToken ct)
    {
        var audioStream = QualitySelector.PickAudioStream(manifest)
            ?? throw new InvalidOperationException("No suitable audio stream was found for this video.");

        await SetStatusAsync(db, item, JobItemStatus.Downloading, 0, ct);

        var audioTemp = Path.Combine(tempDir, $"audio.{audioStream.Container.Name}");
        await DownloadWithProgressAsync(youtube, audioStream, audioTemp, 0.0, 0.7, db, item, ct);

        await SetStatusAsync(db, item, JobItemStatus.Converting, 0.7, ct);

        var codecArgs = item.Job.Format switch
        {
            DownloadFormat.Mp3 => "-codec:a libmp3lame -qscale:a 2",
            DownloadFormat.M4a => "-codec:a aac -b:a 192k",
            DownloadFormat.Wav => "-codec:a pcm_s16le",
            DownloadFormat.Opus => "-codec:a libopus -b:a 160k",
            _ => throw new InvalidOperationException($"Unsupported audio format {item.Job.Format}"),
        };
        await RunFfmpegAsync($"-i \"{audioTemp}\" -y -vn {codecArgs} \"{finalPath}\"", ct);

        if (item.Job.EmbedMetadata && item.Job.Format != DownloadFormat.Wav)
        {
            await SetStatusAsync(db, item, JobItemStatus.Tagging, 0.95, ct);
            TagAudioFile(finalPath, item);
        }
    }

    private static void TagAudioFile(string path, DownloadJobItem item)
    {
        // Same heuristic as the desktop app: "Artist - Title" style video titles
        // split cleanly into performer + track title; otherwise fall back to the
        // channel name as the performer.
        var (artist, title) = SplitArtistTitle(item.Title, item.Author);

        using var file = TagLibFile.Create(path);
        file.Tag.Title = title;
        file.Tag.Performers = [artist];
        file.Tag.Album = item.Author;
        file.Save();
    }

    private static (string Artist, string Title) SplitArtistTitle(string videoTitle, string author)
    {
        var parts = videoTitle.Split(" - ", 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (author, videoTitle);
    }

    private async Task DownloadWithProgressAsync(
        YoutubeClient youtube, IStreamInfo stream, string path,
        double rangeStart, double rangeEnd, AppDbContext db, DownloadJobItem item, CancellationToken ct)
    {
        var current = rangeStart;
        var progress = new Progress<double>(p => current = rangeStart + p * (rangeEnd - rangeStart));

        using var tickerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ticker = TickProgressAsync(() => current, db, item, tickerCts.Token);

        try
        {
            await youtube.Videos.Streams.DownloadAsync(stream, path, progress, ct);
        }
        finally
        {
            tickerCts.Cancel();
            try { await ticker; } catch (OperationCanceledException) { }
        }

        item.Progress = rangeEnd;
        await db.SaveChangesAsync(ct);
        await BroadcastAsync(item, ct);
    }

    private async Task TickProgressAsync(Func<double> getCurrent, AppDbContext db, DownloadJobItem item, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(750, ct);
                item.Progress = getCurrent();
                await db.SaveChangesAsync(ct);
                await BroadcastAsync(item, ct);
            }
        }
        catch (OperationCanceledException) { /* expected on completion */ }
    }

    private async Task SetStatusAsync(AppDbContext db, DownloadJobItem item, JobItemStatus status, double progress, CancellationToken ct)
    {
        item.Status = status;
        item.Progress = progress;
        await db.SaveChangesAsync(ct);
        await BroadcastAsync(item, ct);
    }

    private async Task BroadcastAsync(DownloadJobItem item, CancellationToken ct)
    {
        var dto = new JobItemStatusDto(
            item.Id, item.VideoId, item.Title, item.Author,
            item.Status.ToString(), item.Progress, item.ErrorMessage, item.OutputFileName
        );
        await hub.Clients.Group(ProgressHub.GroupName(item.JobId.ToString()))
            .SendAsync("itemUpdated", dto, ct);
    }

    private async Task RunFfmpegAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with code {process.ExitCode}: {stderr}");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "video" : cleaned;
    }
}
