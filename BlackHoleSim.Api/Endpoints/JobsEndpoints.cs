using BlackHoleSim.Api.Data;
using BlackHoleSim.Api.Jobs;
using BlackHoleSim.Api.Mapping;
using BlackHoleSim.Shared;
using Microsoft.EntityFrameworkCore;

namespace BlackHoleSim.Api.Endpoints;

public static class JobsEndpoints
{
    public static void MapJobsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/jobs", ListJobsAsync)
           .WithName("ListJobs")
           .WithOpenApi();

        app.MapGet("/api/jobs/{id:guid}", GetJobAsync)
           .WithName("GetJob")
           .WithOpenApi();

        app.MapGet("/api/jobs/{id:guid}/image", GetJobImageAsync)
           .WithName("GetJobImage")
           .WithOpenApi();

        app.MapDelete("/api/jobs/{id:guid}", DeleteJobAsync)
           .WithName("DeleteJob")
           .WithOpenApi();
    }

    private static async Task<IResult> ListJobsAsync(
        AppDbContext db,
        HttpContext http,
        int page = 1,
        int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var jobs = await db.RenderJobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Results.Ok(jobs.Select(j => j.ToDto(http.Request)));
    }

    private static async Task<IResult> GetJobAsync(
        Guid id,
        AppDbContext db,
        HttpContext http)
    {
        var entity = await db.RenderJobs.FindAsync(id);
        return entity is null
            ? Results.NotFound()
            : Results.Ok(entity.ToDto(http.Request));
    }

    private static async Task<IResult> GetJobImageAsync(
        Guid id,
        AppDbContext db)
    {
        var entity = await db.RenderJobs
            .Where(j => j.Id == id)
            .Select(j => new { j.Status, j.Png })
            .FirstOrDefaultAsync();

        if (entity is null) return Results.NotFound();
        if (entity.Status != RenderJobStatus.Completed || entity.Png is null)
            return Results.NotFound(new { error = "Image not available yet" });

        return Results.File(entity.Png, "image/png", $"blackhole_{id:N}.png");
    }

    private static async Task<IResult> DeleteJobAsync(
        Guid id,
        AppDbContext db,
        JobCancellationRegistry cancelRegistry)
    {
        var entity = await db.RenderJobs.FindAsync(id);
        if (entity is null) return Results.NotFound();

        // Cancel if currently running
        cancelRegistry.Cancel(id);

        db.RenderJobs.Remove(entity);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
