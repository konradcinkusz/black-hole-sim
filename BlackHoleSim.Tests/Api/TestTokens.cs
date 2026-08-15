using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

// Both packages define JwtRegisteredClaimNames with the same values. The alias picks the one
// belonging to the handler that writes the token here.
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace BlackHoleSim.Tests.Api;

/// <summary>
/// Mints the tokens the identity service would issue, signed with the test factory's key.
/// </summary>
public static class TestTokens
{
    /// <summary>A valid access token for <paramref name="subject"/>.</summary>
    public static string For(BlackHoleSimApiFactory factory, string subject, string? email = null)
        => Build(factory, subject, email,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires:   DateTime.UtcNow.AddMinutes(30));

    /// <summary>A token that was valid and is not any more.</summary>
    /// <remarks>
    /// The window is in the past but still a window. Issuing one where <c>nbf</c> equals
    /// <c>exp</c> is rejected as malformed before expiry is ever considered, which would let
    /// this test pass against an API that had stopped checking lifetimes.
    /// </remarks>
    public static string Expired(BlackHoleSimApiFactory factory, string subject)
        => Build(factory, subject, null,
            notBefore: DateTime.UtcNow.AddMinutes(-60),
            expires:   DateTime.UtcNow.AddMinutes(-30));

    private static string Build(
        BlackHoleSimApiFactory factory, string subject, string? email,
        DateTime notBefore, DateTime expires)
    {

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));

        var token = new JwtSecurityToken(
            issuer: BlackHoleSimApiFactory.Issuer,
            audience: BlackHoleSimApiFactory.Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(
                factory.SigningKey, SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
