namespace BlackHoleSim.Api.Endpoints;

public static class HealthEndpoints
{
    /// <summary>Tag marking the checks that answer "is the process alive", nothing more.</summary>
    public const string LiveTag = "live";

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Readiness: everything — database connectivity and schema state. This is what
        // the platform's deploy check watches, so it must go red while the app is not
        // yet able to serve, and red again if the database goes away.
        app.MapHealthChecks("/health");

        // Liveness: live-tagged checks only, so a database outage restarts nothing.
        // A process that answers here is running; whether it is *useful* is /health.
        app.MapHealthChecks("/alive", new()
        {
            Predicate = r => r.Tags.Contains(LiveTag)
        });

        // Pre-existing paths, kept so the compose healthcheck and any bookmarked URL
        // do not break. /api/health is the readiness check under its old name.
        app.MapHealthChecks("/api/health");
        app.MapHealthChecks("/api/health/db", new()
        {
            Predicate = r => r.Name == "db"
        });
    }
}
