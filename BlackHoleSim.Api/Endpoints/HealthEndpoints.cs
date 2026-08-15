namespace BlackHoleSim.Api.Endpoints;

/// <summary>
/// The health paths that predate the shared kernel.
/// </summary>
/// <remarks>
/// <c>/health</c> and <c>/alive</c> now come from <c>MapDefaultEndpoints</c> in
/// BlackHoleSim.ServiceDefaults, so every service in the estate answers the same two
/// paths with the same semantics. These two remain because they were public: the
/// compose healthcheck, the README and anyone's bookmarks all point at
/// <c>/api/health</c>. They are aliases, not a second opinion — <c>/api/health</c>
/// runs exactly the checks <c>/health</c> runs.
/// </remarks>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/api/health");

        app.MapHealthChecks("/api/health/db", new()
        {
            Predicate = r => r.Name == "db"
        });
    }
}
