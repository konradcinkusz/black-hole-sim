using BlackHoleSim.Api.Auth;
using BlackHoleSim.Api.Data;
using BlackHoleSim.Api.Jobs;
using BlackHoleSim.Api.Mapping;
using BlackHoleSim.Shared;
using Microsoft.AspNetCore.RateLimiting;

namespace BlackHoleSim.Api.Endpoints;

public static class RenderEndpoints
{
    private const int MaxPixels = 2_073_600; // 1920×1080
    private const int MaxSteps  = 20_000;

    public static void MapRenderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/render", PostRenderAsync)
           .RequireAuthorization()
           .RequireRateLimiting("render")
           .WithName("PostRender")
           .WithOpenApi();
    }

    private static async Task<IResult> PostRenderAsync(
        RenderParameters parameters,
        AppDbContext db,
        IRenderJobQueue queue,
        HttpContext http)
    {
        if (parameters.Width * parameters.Height > MaxPixels)
            return Results.BadRequest(new { error = $"Resolution exceeds maximum ({MaxPixels} pixels)" });

        if (parameters.MaxSteps > MaxSteps)
            return Results.BadRequest(new { error = $"MaxSteps exceeds maximum ({MaxSteps})" });

        if (parameters.Rin >= parameters.Rout)
            return Results.BadRequest(new { error = "Rin must be less than Rout" });

        if (parameters.Rout >= parameters.Rcam)
            return Results.BadRequest(new { error = "Rout must be less than Rcam" });

        var entity = new RenderJobEntity
        {
            Id         = Guid.NewGuid(),
            OwnerId    = http.User.OwnerId(),
            Parameters = parameters,
            Status     = RenderJobStatus.Pending,
            CreatedAt  = DateTime.UtcNow,
            Progress   = 0.0
        };

        db.RenderJobs.Add(entity);
        await db.SaveChangesAsync();
        await queue.EnqueueAsync(entity.Id);

        var dto = entity.ToDto(http.Request);
        return Results.Accepted($"/api/jobs/{entity.Id}", dto);
    }
}
