namespace BlackHoleSim.Api.Jobs;

public interface IRenderJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken ct = default);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct);
}
