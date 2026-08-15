using Microsoft.EntityFrameworkCore;

namespace BlackHoleSim.Api.Data;

/// <summary>
/// Applies EF Core migrations after the HTTP listener is up, retrying while the
/// database is still coming up, and opens <see cref="DatabaseReadyGate"/> when done.
/// </summary>
public sealed class DatabaseMigrationService(
    IServiceProvider sp,
    DatabaseReadyGate gate,
    ILogger<DatabaseMigrationService> logger) : BackgroundService
{
    private const int MaxAttempts = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Return control to the host immediately so this never delays Kestrel.
        await Task.Yield();

        for (var attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            TimeSpan delay;

            try
            {
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await db.Database.MigrateAsync(stoppingToken);

                logger.LogInformation("Database migrations applied (attempt {Attempt})", attempt);
                gate.MarkReady();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt == MaxAttempts)
                {
                    // Deliberately not rethrown: an unhandled exception here would stop
                    // the host, and a process that exits cannot report *why* it is
                    // unhealthy. Staying up with a red /health check is more debuggable
                    // and lets the platform surface a failed deploy rather than a crash loop.
                    logger.LogCritical(ex,
                        "Database migration failed after {Attempts} attempts; /health will report unhealthy",
                        MaxAttempts);
                    gate.MarkFailed(ex);
                    return;
                }

                // 2s, 4s, 8s … capped at 30s.
                delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                logger.LogWarning(ex,
                    "Database migration attempt {Attempt}/{Max} failed; retrying in {Delay}",
                    attempt, MaxAttempts, delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
