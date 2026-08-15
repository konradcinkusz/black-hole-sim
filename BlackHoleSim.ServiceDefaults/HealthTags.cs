namespace BlackHoleSim.ServiceDefaults;

/// <summary>
/// Tags that decide which health checks answer which endpoint.
/// </summary>
public static class HealthTags
{
    /// <summary>
    /// Marks a check that answers "is this process alive", and nothing more.
    /// </summary>
    /// <remarks>
    /// Only cheap, dependency-free checks carry this. The split matters because the
    /// two endpoints are consumed by different things with different powers: a
    /// liveness probe usually restarts the container, so a database outage must not
    /// be visible through it — restarting the app does not fix someone else's
    /// database, it just adds an outage to an outage.
    /// </remarks>
    public const string Live = "live";
}
