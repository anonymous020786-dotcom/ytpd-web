namespace YtpdWeb.Api.Models;

public enum JobItemStatus
{
    Queued,
    Resolving,
    Downloading,
    Converting,
    Tagging,
    Completed,
    Failed,
    Cancelled,
}

public enum DownloadFormat
{
    Mp4,
    Mkv,
    Webm,
    Mp3,
    M4a,
    Wav,
    Opus,
}

public class DownloadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SourceUrl { get; set; } = "";
    public DownloadFormat Format { get; set; }
    public string Quality { get; set; } = "best";
    public bool EmbedMetadata { get; set; } = true;

    public List<DownloadJobItem> Items { get; set; } = new();
}

public class DownloadJobItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public DownloadJob Job { get; set; } = null!;

    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";

    public JobItemStatus Status { get; set; } = JobItemStatus.Queued;
    public double Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputFileName { get; set; }
}
