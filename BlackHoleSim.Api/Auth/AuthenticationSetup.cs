using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BlackHoleSim.Api.Auth;

/// <summary>
/// Where tokens come from, and what this service will accept. Bound from <c>Auth:*</c>.
/// </summary>
/// <remarks>
/// There is deliberately no key setting here. This service verifies; it cannot issue.
/// Under RS256 the public half arrives from the identity service's JWKS, so the only
/// thing a deployment supplies is an address.
/// </remarks>
public sealed class AuthOptions
{
    /// <summary>Public base URL of the identity service, e.g. <c>https://blackholesim-auth.fly.dev</c>.</summary>
    public string? Authority { get; set; }

    /// <summary>The <c>iss</c> value tokens must carry — the identity service's <c>Jwt:Issuer</c>.</summary>
    public string Issuer { get; set; } = "BlackHoleSim";

    /// <summary>The <c>aud</c> value tokens must carry — the identity service's <c>Jwt:Audience</c>.</summary>
    public string Audience { get; set; } = "BlackHoleSim";

    /// <summary>
    /// Require HTTPS when fetching the discovery document. Only turn this off where the
    /// identity service is reached over a private network that never leaves the platform
    /// (compose's service network, Fly's <c>.internal</c>).
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}

public static class AuthenticationSetup
{
    /// <summary>
    /// Registers bearer authentication against the identity service named by <c>Auth:Authority</c>.
    /// </summary>
    /// <remarks>
    /// The signing keys are discovered, not configured: <c>MetadataAddress</c> points at the
    /// identity service's discovery document, which names its JWKS, and the handler refreshes
    /// the key set on rotation. This service therefore holds no key material at all — it can
    /// verify a token and it cannot mint one, which is the whole point of the identity service
    /// having moved to RS256 (authservice ADR 0002).
    ///
    /// Discovery is fetched lazily, on the first request that carries a token — not at startup.
    /// A cold start with the identity service still coming up is therefore not a failed boot,
    /// it is a few 401s until the metadata is retrievable.
    /// </remarks>
    public static void AddBlackHoleSimAuthentication(this WebApplicationBuilder builder)
    {
        var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

        // Fail here, naming the setting, rather than starting an API that accepts nothing and
        // says "401" to every caller without explaining why. There is deliberately no
        // "authentication off" mode to fall back to: a second code path in which every job is
        // world-readable is exactly the posture this change exists to remove, and a
        // misconfigured deployment silently taking it would be worse than not booting.
        if (string.IsNullOrWhiteSpace(auth.Authority))
        {
            throw new InvalidOperationException(
                "Auth:Authority is not configured. Set it to the base URL of the identity service " +
                "issuing tokens for this deployment (environment variable Auth__Authority), for " +
                "example http://auth:8080 under docker compose.");
        }

        builder.Services.AddSingleton(auth);

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress =
                    $"{auth.Authority.TrimEnd('/')}/.well-known/openid-configuration";
                options.RequireHttpsMetadata = auth.RequireHttpsMetadata;

                // Leave the claim names as the token spells them. The default inbound map
                // rewrites `sub` to a schemas.xmlsoap.org URI, which then quietly disagrees
                // with the `sub` this service stores as a job's owner.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = auth.Issuer,
                    ValidateAudience         = true,
                    ValidAudience            = auth.Audience,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,

                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = ClaimTypes.Role,

                    // Not zero. The identity service and this one are separate machines with
                    // separately drifting clocks, and a token minted moments ago failing here
                    // as "not yet valid" is a confusing, intermittent sign-in bug. Thirty
                    // seconds absorbs ordinary NTP drift without meaningfully extending the
                    // life of a token that has genuinely expired.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        builder.Services.AddAuthorization();
    }
}
