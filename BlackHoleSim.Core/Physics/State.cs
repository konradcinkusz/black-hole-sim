namespace BlackHoleSim.Core.Physics;

/// <summary>
/// Photon phase-space state: spacetime coordinates (t, r, φ) and their conjugate momenta (pt, pr, pφ).
/// G = c = M = 1 (geometric units).
/// </summary>
public readonly struct State
{
    public readonly double t;
    public readonly double r;
    public readonly double phi;
    public readonly double pt;
    public readonly double pr;
    public readonly double pphi;

    public State(double t, double r, double phi, double pt, double pr, double pphi)
    {
        this.t    = t;
        this.r    = r;
        this.phi  = phi;
        this.pt   = pt;
        this.pr   = pr;
        this.pphi = pphi;
    }

    /// <summary>Returns <c>this + a * k</c> without heap allocation.</summary>
    public State AddScaled(in State k, double a)
        => new(t + a * k.t,
               r + a * k.r,
               phi + a * k.phi,
               pt + a * k.pt,
               pr + a * k.pr,
               pphi + a * k.pphi);

    public static State operator +(in State a, in State b)
        => new(a.t + b.t, a.r + b.r, a.phi + b.phi, a.pt + b.pt, a.pr + b.pr, a.pphi + b.pphi);

    public static State operator *(double s, in State a)
        => new(s * a.t, s * a.r, s * a.phi, s * a.pt, s * a.pr, s * a.pphi);
}
