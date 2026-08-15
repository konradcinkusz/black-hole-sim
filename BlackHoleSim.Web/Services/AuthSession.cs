using Microsoft.AspNetCore.Components.Authorization;

namespace BlackHoleSim.Web.Services;

/// <summary>
/// Sign-in, sign-out and token refresh — the one place that writes the token store and tells
/// the rest of the app the authentication state changed.
/// </summary>
public sealed class AuthSession(
    AuthApiClient auth,
    TokenStore tokens,
    AuthenticationStateProvider stateProvider)
{
    // Serialises refresh. A page that fires several API calls at once (the gallery loading
    // thumbnails, say) would otherwise present the same expired token n times, get n 401s, and
    // race n refreshes against a single-use rotating refresh token — where the first wins and
    // every other is a *replay*, which the identity service is entitled to treat as an attack
    // and kill the whole token family for. One refresh at a time; the rest await its result.
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        var result = await auth.LoginAsync(email, password);
        if (result.Succeeded) await AcceptAsync(result.Tokens!);
        return result;
    }

    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        var result = await auth.RegisterAsync(email, password);
        if (result.Succeeded) await AcceptAsync(result.Tokens!);
        return result;
    }

    public async Task SignOutAsync()
    {
        var refreshToken = await tokens.GetRefreshTokenAsync();

        if (!string.IsNullOrEmpty(refreshToken))
            await auth.LogoutAsync(refreshToken);

        await tokens.ClearAsync();
        NotifyChanged();
    }

    /// <summary>
    /// Exchanges the refresh token for a new pair. Returns the new access token, or null when
    /// the session is over and the caller should stop retrying.
    /// </summary>
    public async Task<string?> TryRefreshAsync(string? staleAccessToken)
    {
        await _refreshLock.WaitAsync();
        try
        {
            // Another caller may have refreshed while this one waited. If the stored token is
            // no longer the one that just failed, it is already the new one — use it rather
            // than spending (and rotating away) a second refresh token.
            var current = await tokens.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(current) && current != staleAccessToken)
                return current;

            var refreshToken = await tokens.GetRefreshTokenAsync();
            if (string.IsNullOrEmpty(refreshToken)) return null;

            var refreshed = await auth.RefreshAsync(refreshToken);

            if (refreshed is null || string.IsNullOrEmpty(refreshed.AccessToken))
            {
                // The refresh token is spent, revoked or expired. Clearing here is what makes
                // the UI fall back to the sign-in page instead of looping on 401s.
                await tokens.ClearAsync();
                NotifyChanged();
                return null;
            }

            await AcceptAsync(refreshed);
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task AcceptAsync(TokenResponse issued)
    {
        await tokens.SaveAsync(issued.AccessToken, issued.RefreshToken);
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (stateProvider is JwtAuthenticationStateProvider provider)
            provider.NotifyStateChanged();
    }
}
