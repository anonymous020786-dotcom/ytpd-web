using System.Collections.Concurrent;

namespace YtpdWeb.Api.Bot;

public record PendingVideo(string VideoId, string Title, string Author, string SourceUrl, DateTimeOffset CreatedAt);

// Telegram's callback_data is capped at 64 bytes, far too small for a video's
// title/author/URL - so buttons carry only a short token, and the actual
// resolved video sits here in memory until the matching button is pressed.
// In-memory and single-instance is fine: this app already runs as exactly
// one EC2 container, same as its in-process DownloadQueue.
public class PendingSelectionCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, PendingVideo> _entries = new();

    public string Add(PendingVideo video)
    {
        Sweep();
        var token = Guid.NewGuid().ToString("N")[..10];
        _entries[token] = video;
        return token;
    }

    public bool TryGet(string token, out PendingVideo video) => _entries.TryGetValue(token, out video!);

    private void Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - Ttl;
        foreach (var (key, value) in _entries)
        {
            if (value.CreatedAt < cutoff) _entries.TryRemove(key, out _);
        }
    }
}
