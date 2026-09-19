namespace YtpdWeb.Api.Services;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string TempPath { get; set; } = "/data/temp";
    public string DownloadsPath { get; set; } = "/data/downloads";

    // Finished files older than this get swept by CleanupService. Keeps a
    // hosted VPS from silently filling its disk with old downloaded media.
    public int RetentionHours { get; set; } = 48;
}

public class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    // "ffmpeg" resolves via PATH for local/server installs; the desktop
    // build points this at its bundled binary instead.
    public string Path { get; set; } = "ffmpeg";
}
