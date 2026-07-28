using BlackHoleSim.Core.Physics;
using BlackHoleSim.Core.Rendering;
using BlackHoleSim.Shared;
using FluentAssertions;

namespace BlackHoleSim.Tests.Rendering;

public class RaytracerSmokeTests
{
    [Fact]
    public void Render16x16_ReturnsCorrectBufferSize()
    {
        var p      = new RenderParameters { Width = 16, Height = 16, MaxSteps = 500 };
        var buffer = Raytracer.RenderToPixels(p);

        buffer.Length.Should().Be(16 * 16 * 3);
    }

    [Fact]
    public void Render16x16_BufferIsNotAllZero()
    {
        var p      = new RenderParameters { Width = 16, Height = 16, MaxSteps = 500 };
        var buffer = Raytracer.RenderToPixels(p);

        buffer.Any(b => b > 0).Should().BeTrue("at least some pixels should have non-zero colour");
    }

    [Fact]
    public void Render16x16_ContainsDiskColour()
    {
        var p = new RenderParameters
        {
            Width = 16, Height = 16, MaxSteps = 1000,
            Rin = 6, Rout = 20, Rcam = 50, BMax = 10
        };
        var buffer = Raytracer.RenderToPixels(p);

        // Disk colour is orange: R=255, G=140, B=30
        // At least one pixel should be orange-ish (R > 200, G > 100, B < 100)
        bool foundDisk = false;
        for (int i = 0; i < buffer.Length; i += 3)
        {
            if (buffer[i] > 200 && buffer[i + 1] > 100 && buffer[i + 2] < 100)
            {
                foundDisk = true;
                break;
            }
        }
        foundDisk.Should().BeTrue("the accretion disk (orange) should appear in the render");
    }

    [Fact]
    public void RenderToPixels_RespectsCancellation()
    {
        var p   = new RenderParameters { Width = 800, Height = 600, MaxSteps = 4000 };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => Raytracer.RenderToPixels(p, null, cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Trace_SmallImpactParameter_IsCapturedByHorizon()
    {
        // Rin/Rout placed beyond Rcam so the ray (which only moves inward
        // from r0) can never cross the disk band — isolates true horizon
        // capture from the disk-hit and invalid-ray cases below.
        var metric = new Schwarzschild();
        var p = new RenderParameters { Rin = 100, Rout = 200, Rcam = 50, MaxSteps = 4000, Step = 0.1 };

        // Well below the photon-sphere critical impact parameter (3*sqrt(3)*M ≈ 5.196M).
        var (r, g, b) = Raytracer.Trace(metric, 3.0, p);

        (r, g, b).Should().Be(((byte)0, (byte)0, (byte)0), "a photon this far inside b_crit must fall into the horizon");
    }

    [Fact]
    public void Trace_ImpactParameterBeyondCameraKinematics_IsSkyNotShadow()
    {
        // At Rcam=50, b greater than Rcam/sqrt(1 - Rs/Rcam) ≈ 51.03 has no
        // physically valid inward-pointing null ray from this camera radius.
        // That's a wide-FOV edge case, not a captured photon, so it must not
        // render as the same colour as the true event-horizon shadow.
        var metric = new Schwarzschild();
        var p = new RenderParameters { Rcam = 50 };

        var (r, g, b) = Raytracer.Trace(metric, 60.0, p);

        (r, g, b).Should().NotBe(((byte)0, (byte)0, (byte)0), "an invalid-geometry ray is not a horizon capture");
    }
}
