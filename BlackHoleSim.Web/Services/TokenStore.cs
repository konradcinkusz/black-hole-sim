using Microsoft.JSInterop;

namespace BlackHoleSim.Web.Services;

/// <summary>
/// The access/refresh pair, cached in memory and persisted to <c>localStorage</c> so a
/// reload does not sign the user out.
/// </summary>
/// <remarks>
/// localStorage is readable by any script running on this origin, so a token here is only as
/// safe as the page is from XSS. The alternative for a WebAssembly client — keeping tokens in
/// memory only — costs a sign-in on every refresh, and the usual "just use an httpOnly cookie"
/// answer needs a server-side session this deployment does not have: the frontend is a static
/// bundle on nginx and the identity service is a separate origin. Access tokens are short-lived
/// for this reason.
/// </remarks>
public sealed class TokenStore(IJSRuntime js)
{
    private const string AccessKey  = "blackholesim.accessToken";
    private const string RefreshKey = "blackholesim.refreshToken";

    private string? _access;
    private string? _refresh;
    private bool _loaded;

    public async ValueTask<string?> GetAccessTokenAsync()
    {
        await EnsureLoadedAsync();
        return _access;
    }

    public async ValueTask<string?> GetRefreshTokenAsync()
    {
        await EnsureLoadedAsync();
        return _refresh;
    }

    public async Task SaveAsync(string accessToken, string refreshToken)
    {
        _access  = accessToken;
        _refresh = refreshToken;
        _loaded  = true;

        await js.InvokeVoidAsync("localStorage.setItem", AccessKey, accessToken);
        await js.InvokeVoidAsync("localStorage.setItem", RefreshKey, refreshToken);
    }

    public async Task ClearAsync()
    {
        _access  = null;
        _refresh = null;
        _loaded  = true;

        await js.InvokeVoidAsync("localStorage.removeItem", AccessKey);
        await js.InvokeVoidAsync("localStorage.removeItem", RefreshKey);
    }

    private async ValueTask EnsureLoadedAsync()
    {
        if (_loaded) return;

        _access  = await js.InvokeAsync<string?>("localStorage.getItem", AccessKey);
        _refresh = await js.InvokeAsync<string?>("localStorage.getItem", RefreshKey);
        _loaded  = true;
    }
}
