namespace BlackHoleSim.Shared;

/// <summary>
/// API representation of a render job (returned from all job endpoints).
/// </summary>
public record RenderJobDto
{
    public Guid Id { get; init; }
    public RenderParameters Parameters { get; init; } = new();
    public RenderJobStatus Status { get; init; }
    public double Progress { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    /// <summary>Link to /api/jobs/{id}/image — only populated when Completed.</summary>
    public string? ImageUrl { get; init; }
}
