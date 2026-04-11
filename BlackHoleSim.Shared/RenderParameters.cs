using System.ComponentModel.DataAnnotations;

namespace BlackHoleSim.Shared;

/// <summary>
/// All parameters needed to reproduce a black-hole render.
/// Defaults reproduce the canonical README example.
/// Uses { get; set; } instead of { get; init; } so Blazor's @bind-Value can write to them.
/// </summary>
public record RenderParameters
{
    /// <summary>Accretion disk inner radius (ISCO = 6M).</summary>
    [Range(3.0, 100.0)]
    public double Rin { get; set; } = 6.0;

    /// <summary>Accretion disk outer radius.</summary>
    [Range(6.0, 200.0)]
    public double Rout { get; set; } = 20.0;

    /// <summary>Camera distance from the black hole (must be > Rout).</summary>
    [Range(10.0, 1000.0)]
    public double Rcam { get; set; } = 50.0;

    /// <summary>RK4 integration step size (smaller = more accurate, slower).</summary>
    [Range(0.01, 2.0)]
    public double Step { get; set; } = 0.25;

    /// <summary>Field-of-view scaling: maximum impact parameter sampled.</summary>
    [Range(1.0, 100.0)]
    public double BMax { get; set; } = 10.0;

    /// <summary>Output image width in pixels.</summary>
    [Range(16, 3840)]
    public int Width { get; set; } = 800;

    /// <summary>Output image height in pixels.</summary>
    [Range(16, 2160)]
    public int Height { get; set; } = 600;

    /// <summary>Maximum RK4 steps per ray before giving up (prevents infinite loops).</summary>
    [Range(100, 50000)]
    public int MaxSteps { get; set; } = 4000;
}
