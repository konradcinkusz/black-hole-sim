using BlackHoleSim.Shared;

namespace BlackHoleSim.Api.Data;

public sealed class RenderJobEntity
{
    public Guid Id { get; set; }
    public RenderParameters Parameters { get; set; } = new();
    public RenderJobStatus Status { get; set; } = RenderJobStatus.Pending;
    public double Progress { get; set; }
    public byte[]? Png { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
