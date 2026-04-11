using BlackHoleSim.Core.Physics;
using FluentAssertions;

namespace BlackHoleSim.Tests.Physics;

public class StateTests
{
    private static State Make(double v = 1.0)
        => new(v, v, v, v, v, v);

    [Fact]
    public void AddScaled_IsLinear()
    {
        var y = Make(1.0);
        var k = Make(2.0);
        const double a = 3.0;

        var result   = y.AddScaled(k, a);
        var expected = y + a * k;

        result.t.Should().BeApproximately(expected.t, 1e-12);
        result.r.Should().BeApproximately(expected.r, 1e-12);
        result.pr.Should().BeApproximately(expected.pr, 1e-12);
    }

    [Fact]
    public void AddScaled_ZeroScaleReturnsSameState()
    {
        var y = Make(5.0);
        var k = Make(99.0);
        var result = y.AddScaled(k, 0.0);

        result.t.Should().Be(y.t);
        result.r.Should().Be(y.r);
        result.phi.Should().Be(y.phi);
    }

    [Fact]
    public void OperatorPlus_IsCommutative()
    {
        var a = new State(1, 2, 3, 4, 5, 6);
        var b = new State(6, 5, 4, 3, 2, 1);

        var ab = a + b;
        var ba = b + a;

        ab.t.Should().Be(ba.t);
        ab.r.Should().Be(ba.r);
        ab.pphi.Should().Be(ba.pphi);
    }

    [Fact]
    public void ScalarMultiply_ScalesAllComponents()
    {
        var s = new State(1, 2, 3, 4, 5, 6);
        var result = 2.0 * s;

        result.t.Should().Be(2.0);
        result.r.Should().Be(4.0);
        result.phi.Should().Be(6.0);
        result.pt.Should().Be(8.0);
        result.pr.Should().Be(10.0);
        result.pphi.Should().Be(12.0);
    }
}
