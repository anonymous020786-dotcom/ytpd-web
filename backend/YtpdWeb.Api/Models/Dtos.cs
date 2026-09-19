namespace YtpdWeb.Api.Models;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username);

public record ResolveRequest(string Url);

public record ResolvedVideoDto(
    string VideoId,
    string Title,
    string Author,
    string ThumbnailUrl,
    double? DurationSeconds
);

public record ResolveResponseDto(
    string Kind, // "Video" | "Playlist" | "Channel"
    string Title,
    string? Author,
    string? ThumbnailUrl,
    List<ResolvedVideoDto> Items,
    bool Truncated
);

public record CreateJobItemDto(string VideoId, string Title, string Author);

public record CreateJobRequest(
    string SourceUrl,
    List<CreateJobItemDto> Items,
    string Format, // maps to DownloadFormat
    string Quality, // "best" | "1080p" | "720p" | ...
    bool EmbedMetadata
);

public record JobItemStatusDto(
    Guid Id,
    string VideoId,
    string Title,
    string Author,
    string Status,
    double Progress,
    string? ErrorMessage,
    string? OutputFileName
);

public record JobStatusDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string SourceUrl,
    string Format,
    string Quality,
    List<JobItemStatusDto> Items
);
