using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlackHoleSim.Web.Services;

/// <summary>
/// Derives the UI's notion of "who is signed in" from the stored access token.
/// </summary>
/// <remarks>
/// The signature is not checked here, and that is not an oversight: a WebAssembly bundle runs
/// on the user's machine, so any check it performs is one the user can skip. These claims decide
/// which buttons to draw, nothing more. The authority on whether a token is genuine is
/// BlackHoleSim.Api, which validates it against the identity service's published keys on every
/// request. A forged token gets a nav bar with the wrong name on it and 401 from every endpoint.
/// </remarks>
public sealed class JwtAuthenticationStateProvider(TokenStore tokens) : AuthenticationStateProvider
{
    private static readonly AuthenticationState SignedOut =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await tokens.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return SignedOut;

        var claims = ReadClaims(token);
        if (claims.Count == 0) return SignedOut;

        // Expiry is read locally so the UI can show a signed-out state the moment the token
        // lapses, rather than looking signed in until the next request comes back 401.
        if (HasExpired(claims)) return SignedOut;

        // "jwt" as the authentication type is what makes the identity count as authenticated —
        // a ClaimsIdentity built without one is anonymous no matter how many claims it holds.
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt", "email", ClaimTypes.Role)));
    }

    public void NotifyStateChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private static bool HasExpired(List<Claim> claims)
    {
        var exp = claims.FirstOrDefault(c => c.Type == "exp")?.Value;

        return long.TryParse(exp, out var seconds)
            && DateTimeOffset.FromUnixTimeSeconds(seconds) <= DateTimeOffset.UtcNow;
    }

    private static List<Claim> ReadClaims(string jwt)
    {
        var claims = new List<Claim>();

        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return claims;

            using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));

            foreach (var property in payload.RootElement.EnumerateObject())
            {
                // A claim can legitimately repeat (roles, organization membership), which JSON
                // spells as an array. Flattening it keeps IsInRole and the organization claims
                // working the same way they do server-side.
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in property.Value.EnumerateArray())
                        claims.Add(new Claim(property.Name, item.ToString()));
                }
                else
                {
                    claims.Add(new Claim(property.Name, property.Value.ToString()));
                }
            }
        }
        catch (Exception e) when (e is JsonException or FormatException)
        {
            // A token this malformed is not a session. Falls through to an empty list, which
            // the caller reads as signed out.
            return [];
        }

        return claims;
    }

    /// <summary>base64url → bytes: '-'/'_' for '+'/'/', and the padding the encoding drops.</summary>
    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');

        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => "",
            _ => throw new FormatException("Invalid base64url segment.")
        };

        return Convert.FromBase64String(padded);
    }
}
