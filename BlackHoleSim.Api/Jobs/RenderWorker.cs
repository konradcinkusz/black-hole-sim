using BlackHoleSim.Api.Data;
using BlackHoleSim.Core.Rendering;
using BlackHoleSim.Shared;
using Microsoft.EntityFrameworkCore;

namespace BlackHoleSim.Api.Jobs;

public sealed class RenderWorker(
    IRenderJobQueue queue,
    JobCancellationRegistry cancelRegistry,
    DatabaseReadyGate databaseReady,
    IServiceProvider sp,
    ILogger<RenderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Migrations run concurrently with this worker now that they no longer block
        // startup, so the tables may not exist yet. Waiting here is what keeps the
        // first query from throwing and taking the whole host down with it.
        try
        {
            await databaseReady.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Schema never became usable; the render worker is standing down. "
                + "The API stays up so /health can report why.");
            return;
        }

        // Recover jobs that were in-flight when the API last stopped
        await RecoverStaleJobsAsync(stoppingToken);

        await foreach (var jobId in queue.ReadAllAsync(stoppingToken))
        {
            await ProcessJobAsync(jobId, stoppingToken);
        }
    }

    private async Task RecoverStaleJobsAsync(CancellationToken ct)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var staleJobs = await db.RenderJobs
            .Where(j => j.Status == RenderJobStatus.Pending
                     || j.Status == RenderJobStatus.Running)
            .Select(j => j.Id)
            .ToListAsync(ct);

        // Mark Running → Pending and re-enqueue all
        await db.RenderJobs
            .Where(j => j.Status == RenderJobStatus.Running)
            .ExecuteUpdateAsync(
                u => u.SetProperty(e => e.Status, RenderJobStatus.Pending)
                      .SetProperty(e => e.Progress, 0.0),
                ct);

        foreach (var id in staleJobs)
            await queue.EnqueueAsync(id, ct);

        if (staleJobs.Count > 0)
            logger.LogInformation("Recovered {Count} stale render jobs", staleJobs.Count);
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = await db.RenderJobs.FindAsync(new object[] { jobId }, stoppingToken);
        if (entity is null)
        {
            logger.LogWarning("Job {JobId} not found in DB, skipping", jobId);
            return;
        }

        var jobCts = cancelRegistry.Register(jobId, stoppingToken);
        try
        {
            entity.Status = RenderJobStatus.Running;
            await db.SaveChangesAsync(stoppingToken);

            double lastReported = 0;
            var progress = new Progress<double>(p =>
            {
                if (p - lastReported < 0.02 && p < 1.0) return;
                lastReported = p;
                // Fire-and-forget progress update with its own scope
                _ = UpdateProgressAsync(jobId, p);
            });

            var rgb = await Task.Run(
                () => Raytracer.RenderToPixels(entity.Parameters, progress, jobCts.Token),
                jobCts.Token);

            var png = PngEncoder.EncodeRgb24(rgb, entity.Parameters.Width, entity.Parameters.Height);

            entity.Png         = png;
            entity.Progress    = 1.0;
            entity.Status      = RenderJobStatus.Completed;
            entity.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(stoppingToken);

            logger.LogInformation("Job {JobId} completed ({W}x{H})",
                jobId, entity.Parameters.Width, entity.Parameters.Height);
        }
        catch (OperationCanceledException)
        {
            entity.Status = RenderJobStatus.Cancelled;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("Job {JobId} cancelled", jobId);
        }
        catch (Exception ex)
        {
            entity.Status       = RenderJobStatus.Failed;
            entity.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogError(ex, "Job {JobId} failed", jobId);
        }
        finally
        {
            cancelRegistry.Release(jobId);
        }
    }

    private async Task UpdateProgressAsync(Guid jobId, double progress)
    {
        try
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RenderJobs
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.Progress, progress));
        }
        catch
        {
            // Progress updates are best-effort; don't crash the worker
        }
    }
}
