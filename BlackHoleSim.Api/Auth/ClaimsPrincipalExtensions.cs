using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BlackHoleSim.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The identity-service user id a render job is filed under — the token's <c>sub</c>.
    /// </summary>
    /// <remarks>
    /// <c>NameIdentifier</c> is accepted as a fallback because the identity service emits both
    /// (<c>TokenService</c> adds <c>sub</c> and <c>ClaimTypes.NameIdentifier</c> with the same
    /// value), and a future change to the inbound claim mapping should not silently reassign
    /// every existing job to a different owner.
    /// </remarks>
    public static string OwnerId(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
              ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        // Unreachable behind RequireAuthorization: a validated token without a subject is not
        // something to paper over with an empty owner, which would file the job under the same
        // id as every other subject-less caller.
        return string.IsNullOrWhiteSpace(id)
            ? throw new InvalidOperationException("The access token carries no 'sub' claim.")
            : id;
    }
}
