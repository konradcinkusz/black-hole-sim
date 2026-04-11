namespace BlackHoleSim.Core.Physics;

/// <summary>
/// Schwarzschild metric in equatorial plane (G = c = M = 1).
/// ds² = -(1-2/r)dt² + (1-2/r)⁻¹dr² + r²dφ²
/// </summary>
public sealed class Schwarzschild : IMetric
{
    public const double M  = 1.0;
    public const double Rs = 2.0 * M; // Schwarzschild radius

    // f(r) = 1 - rs/r
    private static double F(double r) => 1.0 - Rs / r;

    // Inverse metric components
    public double GttInv(double r) => -1.0 / F(r);
    public double GrrInv(double r) => F(r);
    public double GppInv(double r) => 1.0 / (r * r);

    // Radial derivatives of inverse metric components
    private static double DGttInv_dr(double r) => -Rs / (r * r * F(r) * F(r));
    private static double DGrrInv_dr(double r) =>  Rs / (r * r);
    private static double DGppInv_dr(double r) => -2.0 / (r * r * r);

    /// <inheritdoc/>
    public double H(State s)
        => 0.5 * (GttInv(s.r) * s.pt * s.pt
                + GrrInv(s.r) * s.pr * s.pr
                + GppInv(s.r) * s.pphi * s.pphi);

    /// <inheritdoc/>
    public State RHS(State s)
    {
        // dq^μ/dλ = ∂H/∂p_μ = g^{μμ} p_μ  (diagonal metric)
        double dt   = GttInv(s.r) * s.pt;
        double dr   = GrrInv(s.r) * s.pr;
        double dphi = GppInv(s.r) * s.pphi;

        // dp_μ/dλ = -∂H/∂q^μ
        // t and φ are cyclic → dpt = dpphi = 0
        double dpt   = 0.0;
        double dpphi = 0.0;

        // Only r-derivative is non-zero
        double dpr = -0.5 * (DGttInv_dr(s.r) * s.pt * s.pt
                           + DGrrInv_dr(s.r) * s.pr * s.pr
                           + DGppInv_dr(s.r) * s.pphi * s.pphi);

        return new State(dt, dr, dphi, dpt, dpr, dpphi);
    }
}
