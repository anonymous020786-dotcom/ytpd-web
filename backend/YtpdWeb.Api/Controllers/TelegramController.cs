using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YtpdWeb.Api.Bot;
using YtpdWeb.Api.Data;
using YtpdWeb.Api.Models;
using YtpdWeb.Api.Services;

namespace YtpdWeb.Api.Controllers;

// Telegram webhooks carry no JWT - Telegram itself has no way to hold one -
// so this endpoint is [AllowAnonymous] and instead checks the secret token
// Telegram echoes back in a header (set via setWebhook, see TelegramClient).
// A per-chat allowlist (Telegram:AllowedChatIds) is the actual access
// control: anyone can reach this endpoint, but only allowed chat IDs get a
// real response instead of "here's your chat ID, ask the owner to add it."
[ApiController]
[AllowAnonymous]
[Route("api/bot")]
public class TelegramController(
    TelegramClient telegram,
    IOptions<TelegramOptions> telegramOptions,
    YoutubeResolverService resolver,
    PendingSelectionCache pending,
    DownloadQueue queue,
    AppDbContext db,
    IOptions<StorageOptions> storageOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramController> logger
) : ControllerBase
{
    private readonly TelegramOptions _opts = telegramOptions.Value;
    private readonly StorageOptions _storage = storageOptions.Value;

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] TgUpdate update, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_opts.BotToken))
            return Ok(); // bot not configured - silently accept so Telegram stops retrying

        if (Request.Headers["X-Telegram-Bot-Api-Secret-Token"] != _opts.WebhookSecret)
            return Unauthorized();

        try
        {
            if (update.Message is not null)
                await HandleMessageAsync(update.Message, ct);
            else if (update.CallbackQuery is not null)
                await HandleCallbackAsync(update.CallbackQuery, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed handling Telegram update {UpdateId}", update.UpdateId);
        }

        return Ok();
    }

    private async Task HandleMessageAsync(TgMessage message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        if (!IsAllowed(chatId))
        {
            await telegram.SendMessageAsync(chatId,
                $"🔒 This bot isn't open to this chat yet.\nAsk the owner to add <code>{chatId}</code> to Telegram__AllowedChatIds.", ct: ct);
            return;
        }

        var text = message.Text?.Trim() ?? "";
        if (text.StartsWith("/start"))
        {
            await telegram.SendMessageAsync(chatId,
                "👋 Paste a YouTube video link and I'll offer you a format to download.\n\n" +
                "Playlists and channels aren't supported here - use the web app for those: https://ytpd.videodownloaders.cloud", ct: ct);
            return;
        }

        ResolveResponseDto resolved;
        try
        {
            resolved = await resolver.ResolveAsync(text, ct);
        }
        catch (Exception)
        {
            await telegram.SendMessageAsync(chatId, "⚠️ Couldn't recognize that as a YouTube video link.", ct: ct);
            return;
        }

        if (resolved.Kind != "Video")
        {
            await telegram.SendMessageAsync(chatId,
                $"📃 That's a {resolved.Kind.ToLowerInvariant()} ({resolved.Items.Count} videos) - this bot only handles single videos.\n" +
                "Use the web app for playlists/channels: https://ytpd.videodownloaders.cloud", ct: ct);
            return;
        }

        var video = resolved.Items[0];
        var token = pending.Add(new PendingVideo(video.VideoId, video.Title, video.Author, text, DateTimeOffset.UtcNow));

        var keyboard = new TgInlineKeyboardMarkup(new List<List<TgInlineKeyboardButton>>
        {
            new()
            {
                new TgInlineKeyboardButton("🎬 MP4 (video)", $"dl:{token}:mp4"),
                new TgInlineKeyboardButton("🎵 MP3 (audio)", $"dl:{token}:mp3"),
                new TgInlineKeyboardButton("🎧 M4A (audio)", $"dl:{token}:m4a"),
            },
        });

        await telegram.SendMessageAsync(chatId, $"🎥 <b>{System.Net.WebUtility.HtmlEncode(video.Title)}</b>\n{System.Net.WebUtility.HtmlEncode(video.Author)}\n\nPick a format:", keyboard, ct);
    }

    private async Task HandleCallbackAsync(TgCallbackQuery callback, CancellationToken ct)
    {
        await telegram.AnswerCallbackQueryAsync(callback.Id, ct: ct);

        if (callback.Message is null) return;
        var chatId = callback.Message.Chat.Id;
        var messageId = callback.Message.MessageId;

        if (!IsAllowed(chatId)) return;

        var parts = (callback.Data ?? "").Split(':');
        if (parts.Length != 3 || parts[0] != "dl" || !pending.TryGet(parts[1], out var video))
        {
            await telegram.EditMessageTextAsync(chatId, messageId, "⌛ This link expired - paste the URL again.", ct);
            return;
        }

        var formatCode = parts[2];
        var format = formatCode switch
        {
            "mp4" => DownloadFormat.Mp4,
            "mp3" => DownloadFormat.Mp3,
            "m4a" => DownloadFormat.M4a,
            _ => DownloadFormat.Mp4,
        };

        var job = new DownloadJob
        {
            SourceUrl = video.SourceUrl,
            Format = format,
            Quality = "best",
            EmbedMetadata = true,
        };
        var item = new DownloadJobItem
        {
            JobId = job.Id,
            Job = job,
            VideoId = video.VideoId,
            Title = video.Title,
            Author = video.Author,
        };
        job.Items = [item];

        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(item.Id, ct);

        await telegram.EditMessageTextAsync(chatId, messageId, $"⏳ Downloading <b>{System.Net.WebUtility.HtmlEncode(video.Title)}</b>...", ct);

        // The webhook request scope ends the moment this method returns (it
        // already answered Telegram above) - this needs its own scope to
        // keep polling and eventually send the finished file.
        _ = Task.Run(() => TrackAndDeliverAsync(item.Id, chatId, messageId, video.Title));
    }

    private async Task TrackAndDeliverAsync(Guid itemId, long chatId, long messageId, string title)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lastReportedProgress = -1.0;
        var deadline = DateTimeOffset.UtcNow.AddMinutes(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(3000);

            var item = await scopedDb.JobItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
            if (item is null) return;

            if (item.Status is JobItemStatus.Completed)
            {
                var path = System.IO.Path.Combine(_storage.DownloadsPath, item.JobId.ToString(), item.OutputFileName!);
                try
                {
                    await telegram.SendDocumentByPathAsync(chatId, path, title);
                    await telegram.EditMessageTextAsync(chatId, messageId, $"✅ Sent <b>{System.Net.WebUtility.HtmlEncode(title)}</b>.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send finished file for job item {ItemId}", itemId);
                    await telegram.EditMessageTextAsync(chatId, messageId, $"⚠️ Downloaded but couldn't send the file (it may exceed Telegram's 2GB limit).");
                }
                return;
            }

            if (item.Status is JobItemStatus.Failed)
            {
                await telegram.EditMessageTextAsync(chatId, messageId, $"❌ Failed: {System.Net.WebUtility.HtmlEncode(item.ErrorMessage ?? "unknown error")}");
                return;
            }

            if (item.Status is JobItemStatus.Cancelled)
            {
                await telegram.EditMessageTextAsync(chatId, messageId, "🚫 Cancelled.");
                return;
            }

            var roundedProgress = Math.Round(item.Progress, 1);
            if (roundedProgress != lastReportedProgress)
            {
                lastReportedProgress = roundedProgress;
                await telegram.EditMessageTextAsync(chatId, messageId,
                    $"⏳ {item.Status} <b>{System.Net.WebUtility.HtmlEncode(title)}</b>... {roundedProgress:P0}");
            }
        }

        await telegram.EditMessageTextAsync(chatId, messageId, "⌛ Timed out waiting for this download.");
    }

    private bool IsAllowed(long chatId)
    {
        var allowed = _opts.AllowedChatIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Contains(chatId.ToString());
    }
}
