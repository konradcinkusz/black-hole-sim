using BlackHoleSim.Core.Physics;
using FluentAssertions;
using RK4 = BlackHoleSim.Core.Math.RK4;

namespace BlackHoleSim.Tests.Math;

/// <summary>
/// Tests RK4 order of convergence on a harmonic oscillator: q'' = -q.
/// </summary>
public class RK4ConvergenceTests
{
    // Encode harmonic oscillator as a State: q → r, p → pr. Other fields unused.
    private static State HarmonicRHS(State s) => new(0, s.pr, 0, 0, -s.r, 0);

    private static double RunOscillator(double h, double tEnd)
    {
        var state = new State(0, 1.0, 0, 0, 0, 0); // q=1, p=0
        int steps = (int)(tEnd / h);
        for (int i = 0; i < steps; i++)
            state = RK4.Step(HarmonicRHS, state, h);
        // Exact: q(t) = cos(t)
        return System.Math.Abs(state.r - System.Math.Cos(steps * h));
    }

    [Fact]
    public void HarmonicOscillator_Order4_Convergence()
    {
        // Halving the step size should reduce error by ~16× for a 4th-order method
        double err1 = RunOscillator(0.1,  2.0);
        double err2 = RunOscillator(0.05, 2.0);

        double ratio = err1 / err2;
        ratio.Should().BeGreaterThan(10,
            "halving h should reduce error by ~16× for RK4 (O(h^4))");
    }

    [Fact]
    public void HarmonicOscillator_SmallStep_LowAbsoluteError()
    {
        double err = RunOscillator(0.01, 2.0);
        err.Should().BeLessThan(1e-8, "RK4 should be highly accurate with h=0.01");
    }
}
