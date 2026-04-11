namespace BlackHoleSim.Core.Physics;

/// <summary>
/// Spacetime metric contract for Hamiltonian ray integration.
/// </summary>
public interface IMetric
{
    /// <summary>Hamiltonian H = ½ g^{μν} p_μ p_ν (should equal 0 for null geodesics).</summary>
    double H(in State s);

    /// <summary>Hamilton's equations: returns d(state)/dλ.</summary>
    State RHS(in State s);
}
