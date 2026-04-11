namespace BlackHoleSim.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/api/health");
        app.MapHealthChecks("/api/health/db");
    }
}
