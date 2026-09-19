using System.Collections.Concurrent;
using System.Threading.Channels;

namespace YtpdWeb.Api.Services;

public class DownloadQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _inFlight = new();

    public ValueTask EnqueueAsync(Guid jobItemId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(jobItemId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    // Called by the worker right before it starts processing an item, so a
    // client-initiated cancel has something live to signal.
    public CancellationTokenSource BeginTracking(Guid itemId, CancellationToken linkedTo)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedTo);
        _inFlight[itemId] = cts;
        return cts;
    }

    public void EndTracking(Guid itemId) => _inFlight.TryRemove(itemId, out _);

    // Returns true if an in-flight download was actually signalled; false
    // means the item wasn't running (e.g. still queued, or already done) and
    // the caller should handle that case by updating status directly instead.
    public bool TryCancel(Guid itemId)
    {
        if (!_inFlight.TryGetValue(itemId, out var cts)) return false;
        cts.Cancel();
        return true;
    }
}
