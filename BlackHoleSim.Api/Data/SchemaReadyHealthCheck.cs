using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlackHoleSim.Api.Data;

/// <summary>
/// Readiness check reporting whether migrations have been applied yet.
/// </summary>
/// <remarks>
/// This is the half of the health story a plain database-connectivity check cannot
/// tell: Postgres can be perfectly reachable while the schema this build expects has
/// not been applied, and every request touching a new column would fail against an
/// otherwise green check.
/// </remarks>
public sealed class SchemaReadyHealthCheck(DatabaseReadyGate gate) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (gate.IsReady)
            return Task.FromResult(HealthCheckResult.Healthy("Migrations applied."));

        if (gate.Failure is { } failure)
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Database migration failed; the schema is not usable.", failure));

        // Unhealthy, not Degraded: Degraded still returns 200, which would let the
        // platform route traffic at a schema that is not there yet. The deploy is
        // meant to wait this out inside grace_period.
        return Task.FromResult(HealthCheckResult.Unhealthy("Migrations still running."));
    }
}
