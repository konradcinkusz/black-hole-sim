using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace BlackHoleSim.ServiceDefaults;

/// <summary>
/// Cross-cutting plumbing shared by every service: telemetry, health, service
/// discovery and HTTP resilience.
/// </summary>
/// <remarks>
/// This is a shared <em>kernel</em>, not a shared <em>domain</em>. Nothing about black
/// holes, render jobs or accretion disks belongs here — those live in the service that
/// owns them. The contents are deliberately all extension methods over
/// <see cref="IHostApplicationBuilder"/> and <see cref="WebApplication"/>: a service
/// opts in line by line, with no base class to inherit and nothing applied behind its
/// back.
/// </remarks>
public static class Extensions
{
    /// <summary>
    /// Telemetry, health checks, service discovery and resilient HTTP defaults.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retries, circuit breaker and timeouts on every outbound HttpClient by
            // default. Opt-out is possible per client; opt-in never happens reliably.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Traces, metrics and logs, exported over OTLP when an endpoint is configured.
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(o =>
                       {
                           // Health probes run every few seconds forever. Left in, they
                           // become the overwhelming majority of spans and push the
                           // traces anyone actually wants out of the retention window.
                           o.Filter = context =>
                               !IsHealthProbe(context.Request.Path);
                       })
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    // StartsWithSegments rather than string.StartsWith: it matches on path segments, so
    // "/health" and "/health/db" are probes while a hypothetical "/healthcheck-report"
    // endpoint would not be silently swallowed. It is also null-safe on an empty path.
    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments("/health")
        || path.StartsWithSegments("/alive")
        || path.StartsWithSegments("/api/health");

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // Only wired up when there is somewhere to send it. Registering the exporter
        // unconditionally means every local `dotnet run` spends its life retrying a
        // connection to a collector that was never started — which is exactly the kind
        // of optional dependency that should degrade quietly rather than shout.
        var otlpConfigured = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (otlpConfigured)
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// The one check every service has: "the process is up".
    /// </summary>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [HealthTags.Live]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> (readiness — every check) and <c>/alive</c> (liveness —
    /// only <see cref="HealthTags.Live"/> checks).
    /// </summary>
    /// <remarks>
    /// Both are mapped unconditionally, unlike the Aspire template which restricts them
    /// to Development. The platform health check has to reach <c>/health</c> in
    /// production or the deploy cannot be judged at all. They expose no data beyond
    /// healthy/unhealthy, so there is nothing here to withhold.
    /// </remarks>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");

        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Live)
        });

        return app;
    }
}
