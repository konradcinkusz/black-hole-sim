namespace BlackHoleSim.Api.Data;

/// <summary>
/// Signals when the schema is ready to be queried.
/// </summary>
/// <remarks>
/// Migrations used to run inline before <c>app.Run()</c>, which meant Kestrel
/// only started listening once Postgres had answered. On a platform that decides
/// whether a deploy succeeded from an HTTP health check, that turns a slow (or
/// briefly unreachable) database into a failed deploy rather than a slow one.
///
/// Migrations now run in <see cref="DatabaseMigrationService"/> after the listener
/// is up, and this gate keeps the ordering that made the inline call necessary in
/// the first place: <c>RenderWorker</c> queries <c>RenderJobs</c> as its very first
/// action, and an unhandled exception in a <c>BackgroundService</c> stops the whole
/// host. It waits here instead.
/// </remarks>
public sealed class DatabaseReadyGate
{
    private readonly TaskCompletionSource _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>True once migrations have been applied successfully.</summary>
    public bool IsReady { get; private set; }

    /// <summary>The failure that left the schema unusable, if there was one.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Completes when the schema is ready; faults if migration gave up.</summary>
    public Task WaitAsync(CancellationToken ct) => _tcs.Task.WaitAsync(ct);

    public void MarkReady()
    {
        IsReady = true;
        _tcs.TrySetResult();
    }

    public void MarkFailed(Exception ex)
    {
        Failure = ex;
        _tcs.TrySetException(ex);
    }
}
