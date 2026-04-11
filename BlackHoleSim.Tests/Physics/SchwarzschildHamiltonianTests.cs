using BlackHoleSim.Core.Math;
using BlackHoleSim.Core.Physics;
using FluentAssertions;

namespace BlackHoleSim.Tests.Physics;

public class SchwarzschildHamiltonianTests
{
    [Fact]
    public void Hamiltonian_StaysNearZeroAlongGeodesic()
    {
        var metric = new Schwarzschild();

        // Photon at r=10M, impact parameter b=5M (typical trajectory that escapes)
        double r0   = 10.0;
        double b    = 5.0;
        double f0   = 1.0 - Schwarzschild.Rs / r0;
        double pt   = 1.0;
        double pphi = b;
        double inner = 1.0 / f0 - b * b / (r0 * r0);
        double pr   = -Math.Sqrt(Math.Max(0, f0 * inner));

        var state = new State(0, r0, 0, pt, pr, pphi);

        double maxH = 0;
        for (int i = 0; i < 10_000; i++)
        {
            state = RK4.Step(metric.RHS, state, 0.1);
            double h = Math.Abs(metric.H(state));
            if (h > maxH) maxH = h;

            // Stop if escaped or captured
            if (state.r > 50 || state.r < Schwarzschild.Rs + 0.1) break;
        }

        maxH.Should().BeLessThan(1e-4,
            "Hamiltonian constraint should remain near zero along a null geodesic");
    }

    [Fact]
    public void CyclicMomenta_AreConserved()
    {
        // pt and pphi are conserved (cyclic coordinates t and φ)
        var metric = new Schwarzschild();
        double r0   = 15.0;
        double b    = 3.0;
        double f0   = 1.0 - Schwarzschild.Rs / r0;
        double pt   = 1.0;
        double pphi = b;
        double inner = 1.0 / f0 - b * b / (r0 * r0);
        double pr   = -Math.Sqrt(Math.Max(0, f0 * inner));

        var state = new State(0, r0, 0, pt, pr, pphi);
        double pt0   = state.pt;
        double pphi0 = state.pphi;

        for (int i = 0; i < 5_000; i++)
        {
            state = RK4.Step(metric.RHS, state, 0.1);
            if (state.r > 50 || state.r < Schwarzschild.Rs + 0.1) break;
        }

        state.pt.Should().BeApproximately(pt0, 1e-10, "pt is conserved (energy)");
        state.pphi.Should().BeApproximately(pphi0, 1e-10, "pphi is conserved (angular momentum)");
    }
}
