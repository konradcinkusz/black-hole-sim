using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace BlackHoleSim.Api.Jobs;

public sealed class ChannelRenderJobQueue : IRenderJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(
        new BoundedChannelOptions(100)
        {
            FullMode  = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    public async ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default)
        => await _channel.Writer.WriteAsync(jobId, ct);

    public async IAsyncEnumerable<Guid> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(ct))
            yield return id;
    }
}
