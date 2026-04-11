using BlackHoleSim.Core.Physics;
using FluentAssertions;
using RK4 = BlackHoleSim.Core.Math.RK4;

namespace BlackHoleSim.Tests.Physics;

public class SchwarzschildHamiltonianTests
{
    // Helper: compute pr for a null geodesic with given r0, b (impact parameter), pt=1
    private static double NullPr(double r0, double b)
    {
        double f0    = 1.0 - Schwarzschild.Rs / r0;
        double inner = 1.0 / f0 - b * b / (r0 * r0);
        return -System.Math.Sqrt(System.Math.Max(0.0, inner / f0));
    }

    [Fact]
    public void Hamiltonian_StaysNearZeroAlongGeodesic()
    {
        var metric = new Schwarzschild();

        // Escaping photon: b=8 > b_crit≈5.196, starts at r0=20, small step for accuracy
        double r0   = 20.0;
        double b    = 8.0;
        double pt   = 1.0;
        double pphi = b;
        double pr   = NullPr(r0, b);

        var state = new State(0, r0, 0, pt, pr, pphi);

        double maxH = 0;
        for (int i = 0; i < 20_000; i++)
        {
            state = RK4.Step(metric.RHS, state, 0.05);
            double h = System.Math.Abs(metric.H(state));
            if (h > maxH) maxH = h;

            if (state.r > 100 || state.r < Schwarzschild.Rs + 0.1) break;
        }

        maxH.Should().BeLessThan(1e-4,
            "Hamiltonian constraint should remain near zero along a null geodesic");
    }

    [Fact]
    public void CyclicMomenta_AreConserved()
    {
        // pt and pphi are conserved (cyclic coordinates t and φ)
        var metric = new Schwarzschild();

        // Escaping photon: b=10, r0=30
        double r0   = 30.0;
        double b    = 10.0;
        double pt   = 1.0;
        double pphi = b;
        double pr   = NullPr(r0, b);

        var state = new State(0, r0, 0, pt, pr, pphi);
        double pt0   = state.pt;
        double pphi0 = state.pphi;

        for (int i = 0; i < 10_000; i++)
        {
            state = RK4.Step(metric.RHS, state, 0.05);
            if (state.r > 100 || state.r < Schwarzschild.Rs + 0.1) break;
        }

        state.pt.Should().BeApproximately(pt0, 1e-10, "pt is conserved (energy)");
        state.pphi.Should().BeApproximately(pphi0, 1e-10, "pphi is conserved (angular momentum)");
    }
}
