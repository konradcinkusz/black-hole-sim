using BlackHoleSim.Shared;

namespace BlackHoleSim.Api.Data;

public sealed class RenderJobEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The identity service's user id (the token's <c>sub</c>) that submitted this render.
    /// </summary>
    /// <remarks>
    /// Nullable only because rows predating authentication have no owner to name. Those are
    /// visible to nobody — every query filters on the caller's id — and the alternative,
    /// backfilling them onto some sentinel account, would hand one arbitrary user everyone
    /// else's renders. They can be dropped with a single DELETE; see the README.
    /// </remarks>
    public string? OwnerId { get; set; }
    public RenderParameters Parameters { get; set; } = new();
    public RenderJobStatus Status { get; set; } = RenderJobStatus.Pending;
    public double Progress { get; set; }
    public byte[]? Png { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
