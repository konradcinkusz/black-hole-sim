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
}
