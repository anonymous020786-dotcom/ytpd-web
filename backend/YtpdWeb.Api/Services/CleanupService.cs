using Microsoft.Extensions.Options;

namespace YtpdWeb.Api.Services;

// Sweeps finished downloads older than the retention window. Since this app
// hosts copyrighted media only transiently for the requesting user to grab,
// we don't want completed files sitting on the VPS disk indefinitely.
public class CleanupService(IOptions<StorageOptions> storageOptions, ILogger<CleanupService> logger) : BackgroundService
{
    private readonly StorageOptions _storage = storageOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            SweepOnce();
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void SweepOnce()
    {
        if (!Directory.Exists(_storage.DownloadsPath)) return;

        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(_storage.RetentionHours);
        foreach (var dir in Directory.GetDirectories(_storage.DownloadsPath))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                {
                    Directory.Delete(dir, true);
                    logger.LogInformation("Cleaned up expired download folder {Dir}", dir);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean up {Dir}", dir);
            }
        }
    }
}
