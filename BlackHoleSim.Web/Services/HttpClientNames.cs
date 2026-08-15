namespace BlackHoleSim.Web.Services;

/// <summary>
/// The two backends this frontend talks to. They are separate origins with separate
/// concerns: one issues tokens, the other renders black holes and only verifies them.
/// </summary>
public static class HttpClientNames
{
    /// <summary>BlackHoleSim.Api. Carries the bearer token and refreshes it on 401.</summary>
    public const string Api = "api";

    /// <summary>The identity service. Never carries a bearer token — it issues them.</summary>
    public const string Auth = "auth";
}
