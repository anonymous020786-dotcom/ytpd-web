using System.Threading.Channels;

namespace YtpdWeb.Api.Services;

public class DownloadQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid jobItemId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(jobItemId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
