using System.Collections.Concurrent;

namespace BlackHoleSim.Api.Jobs;

public sealed class JobCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _map = new();

    public CancellationTokenSource Register(Guid jobId, CancellationToken linkedToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        _map[jobId] = cts;
        return cts;
    }

    public bool Cancel(Guid jobId)
    {
        if (_map.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public void Release(Guid jobId)
    {
        if (_map.TryRemove(jobId, out var cts))
            cts.Dispose();
    }
}
