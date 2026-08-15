using BlackHoleSim.Api.Auth;
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
        // A render is private to the account that submitted it. Every handler below therefore
        // filters on the caller's id rather than trusting the job id alone: a GUID is hard to
        // guess, but "hard to guess" is not an access control, and the gallery used to hand
        // every caller a paginated list of them.
        //
        // Someone else's job answers 404, not 403. 403 confirms the id names a real render and
        // turns the list endpoint's old behaviour into an enumeration oracle; the caller cannot
        // distinguish "not yours" from "not a job", which is the whole point.
        var jobs = app.MapGroup("/api/jobs").RequireAuthorization();

        // "" rather than "/": a group pattern of "/" would register /api/jobs/ and leave the
        // unslashed /api/jobs the client actually calls returning 404.
        jobs.MapGet("", ListJobsAsync)
            .WithName("ListJobs")
            .WithOpenApi();

        jobs.MapGet("/{id:guid}", GetJobAsync)
            .WithName("GetJob")
            .WithOpenApi();

        jobs.MapGet("/{id:guid}/image", GetJobImageAsync)
            .WithName("GetJobImage")
            .WithOpenApi();

        jobs.MapDelete("/{id:guid}", DeleteJobAsync)
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

        var ownerId = http.User.OwnerId();

        var jobs = await db.RenderJobs
            .Where(j => j.OwnerId == ownerId)
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
        var ownerId = http.User.OwnerId();

        var entity = await db.RenderJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.OwnerId == ownerId);

        return entity is null
            ? Results.NotFound()
            : Results.Ok(entity.ToDto(http.Request));
    }

    private static async Task<IResult> GetJobImageAsync(
        Guid id,
        AppDbContext db,
        HttpContext http)
    {
        var ownerId = http.User.OwnerId();

        var entity = await db.RenderJobs
            .Where(j => j.Id == id && j.OwnerId == ownerId)
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
        HttpContext http,
        JobCancellationRegistry cancelRegistry)
    {
        var ownerId = http.User.OwnerId();

        var entity = await db.RenderJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.OwnerId == ownerId);

        if (entity is null) return Results.NotFound();

        // Cancel if currently running
        cancelRegistry.Cancel(id);

        db.RenderJobs.Remove(entity);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
