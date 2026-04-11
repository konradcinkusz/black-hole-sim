using BlackHoleSim.Core.Physics;

namespace BlackHoleSim.Core.Math;

/// <summary>
/// Classic 4th-order Runge–Kutta integrator for <see cref="State"/>.
/// </summary>
public static class RK4
{
    /// <summary>
    /// Advances state <paramref name="y"/> by affine-parameter step <paramref name="h"/>
    /// using the derivative function <paramref name="f"/>.
    /// </summary>
    public static State Step(Func<State, State> f, in State y, double h)
    {
        var k1 = f(y);
        var k2 = f(y.AddScaled(k1, h * 0.5));
        var k3 = f(y.AddScaled(k2, h * 0.5));
        var k4 = f(y.AddScaled(k3, h));

        // y + (h/6) * (k1 + 2*k2 + 2*k3 + k4)
        var sum = k1 + (2.0 * k2) + (2.0 * k3) + k4;
        return y.AddScaled(sum, h / 6.0);
    }
}
