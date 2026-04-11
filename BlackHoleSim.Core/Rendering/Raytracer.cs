using System.Threading;
using BlackHoleSim.Core.Math;
using BlackHoleSim.Core.Physics;
using BlackHoleSim.Shared;

namespace BlackHoleSim.Core.Rendering;

/// <summary>
/// Shoots photon rays through Schwarzschild spacetime and produces RGB pixel data.
/// </summary>
public static class Raytracer
{
    // Background gradient (dark blue sky)
    private static readonly (byte R, byte G, byte B) Sky      = (20,  30,  60);
    // Accretion disk color (orange glow)
    private static readonly (byte R, byte G, byte B) DiskColor = (255, 140, 30);
    // Black hole shadow
    private static readonly (byte R, byte G, byte B) Black     = (0,   0,   0);

    /// <summary>
    /// API path: renders into a raw RGB24 buffer and returns it. No disk I/O.
    /// Thread-safe; uses Parallel.For for row-level parallelism.
    /// </summary>
    public static byte[] RenderToPixels(
        RenderParameters p,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var buffer   = new byte[p.Width * p.Height * 3];
        var metric   = new Schwarzschild();
        int rowsDone = 0;

        Parallel.For(0, p.Height, new ParallelOptions { CancellationToken = ct }, j =>
        {
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i < p.Width; i++)
            {
                double b = MapPixelToImpact(i, j, p);
                var (r, g, bl) = Trace(metric, b, p);
                int idx = (j * p.Width + i) * 3;
                buffer[idx]     = r;
                buffer[idx + 1] = g;
                buffer[idx + 2] = bl;
            }
            int done = Interlocked.Increment(ref rowsDone);
            progress?.Report((double)done / p.Height);
        });

        return buffer;
    }

    /// <summary>
    /// Console path: writes a .ppm file to <paramref name="path"/>.
    /// </summary>
    public static void RenderPPM(
        string path,
        int width,
        int height,
        double bMax,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var p = new RenderParameters
        {
            Width = width, Height = height, BMax = bMax
        };
        var metric = new Schwarzschild();

        using var sw = new StreamWriter(path);
        PPMWriter.WriteHeader(sw, width, height);

        for (int j = 0; j < height; j++)
        {
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i < width; i++)
            {
                double b = MapPixelToImpact(i, j, p);
                var (r, g, bl) = Trace(metric, b, p);
                PPMWriter.WritePixel(sw, r, g, bl);
            }
            progress?.Report((double)(j + 1) / height);
        }
    }

    /// <summary>
    /// Integrates a single photon ray and returns its RGB colour.
    /// </summary>
    public static (byte R, byte G, byte B) Trace(IMetric metric, double bImpact, RenderParameters p)
    {
        // Initial state: photon at camera, aimed inward with impact parameter b
        // pt set so H ≈ 0 with pr = -1 (inward), pphi = b·pt
        // For Schwarzschild: H = 0  →  -pt²/f + f·pr² + b²·pt²/r² = 0
        // Simplified: use pt = 1, pr = -sqrt(f·(1/f - b²/r²)), pphi = b
        double r0   = p.Rcam;
        double f0   = 1.0 - Schwarzschild.Rs / r0;
        double pt   = 1.0;
        double pphi = bImpact;

        // pr from null-geodesic condition H=0
        double inner = 1.0 / f0 - bImpact * bImpact / (r0 * r0);
        if (inner < 0) return Black; // would not propagate
        double pr = -System.Math.Sqrt(f0 * inner);

        var state = new State(0.0, r0, 0.0, pt, pr, pphi);

        Func<State, State> rhs = metric.RHS;

        for (int step = 0; step < p.MaxSteps; step++)
        {
            state = RK4.Step(rhs, state, p.Step);
            double r = state.r;

            // Captured by event horizon
            if (r <= Schwarzschild.Rs + 1e-4)
                return Black;

            // Hit accretion disk (equatorial plane crossing handled implicitly — thin disk approx)
            if (r >= p.Rin && r <= p.Rout)
                return DiskColor;

            // Escaped back past camera
            if (r >= p.Rcam)
                return GetSkyColor(state.phi);
        }

        // Didn't terminate — treat as escaped
        return GetSkyColor(state.phi);
    }

    private static double MapPixelToImpact(int i, int j, RenderParameters p)
    {
        // Map pixel to [-bMax, +bMax] with (0,0) at centre
        double x = (i - p.Width  / 2.0) / (p.Width  / 2.0) * p.BMax;
        double y = (j - p.Height / 2.0) / (p.Height / 2.0) * p.BMax;
        return System.Math.Sqrt(x * x + y * y);
    }

    private static (byte R, byte G, byte B) GetSkyColor(double phi)
    {
        // Faint star-field tint based on azimuthal angle
        double t = 0.5 + 0.5 * System.Math.Sin(phi * 3.0);
        byte r = (byte)(Sky.R + (int)(20 * t));
        byte g = (byte)(Sky.G + (int)(10 * t));
        byte b = Sky.B;
        return (r, g, b);
    }
}
