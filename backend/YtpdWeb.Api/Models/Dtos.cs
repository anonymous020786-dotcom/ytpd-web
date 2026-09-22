namespace YtpdWeb.Api.Models;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, string Username);

public record ResolveRequest(string Url);

public record ResolvedVideoDto(
    string VideoId,
    string Title,
    string Author,
    string ThumbnailUrl,
    double? DurationSeconds,
    // Real available video heights (e.g. [1080, 720, 480]), highest first.
    // Only populated for a single-video resolve - fetching each video's
    // full stream manifest during a playlist/channel resolve would be far
    // too slow, so those items get an empty list and callers fall back to
    // a generic quality list instead.
    List<int> AvailableVideoQualities
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
