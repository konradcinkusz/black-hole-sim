using BlackHoleSim.Api.Data;
using BlackHoleSim.Shared;

namespace BlackHoleSim.Api.Mapping;

public static class RenderJobMapping
{
    public static RenderJobDto ToDto(this RenderJobEntity e, HttpRequest? request = null)
    {
        string? imageUrl = null;
        if (e.Status == RenderJobStatus.Completed && request is not null)
        {
            imageUrl = $"{request.Scheme}://{request.Host}/api/jobs/{e.Id}/image";
        }

        return new RenderJobDto
        {
            Id           = e.Id,
            Parameters   = e.Parameters,
            Status       = e.Status,
            Progress     = e.Progress,
            ErrorMessage = e.ErrorMessage,
            CreatedAt    = e.CreatedAt,
            CompletedAt  = e.CompletedAt,
            ImageUrl     = imageUrl
        };
    }
}
