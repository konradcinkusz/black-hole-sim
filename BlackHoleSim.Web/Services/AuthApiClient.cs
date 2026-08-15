using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BlackHoleSim.Web.Services;

// The wire contract of the identity service (konradcinkusz/authservice), only the parts this
// frontend uses. Kept as local records rather than a shared package: the whole point of the
// service being consumed over HTTP is that this repo takes no source-level dependency on it.
public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType);
public record ConsentVersionsResponse(string Terms, string Privacy, string Cookies);

/// <summary>Outcome of a sign-in or registration attempt.</summary>
/// <param name="Tokens">The issued pair, or null when the attempt did not produce one.</param>
/// <param name="Error">A message to show the user, or null on success.</param>
/// <param name="PendingVerification">
/// Registration succeeded but the account must confirm its email address before it can sign in.
/// The identity service answers 202 in that case rather than issuing tokens.
/// </param>
public record AuthResult(TokenResponse? Tokens, string? Error, bool PendingVerification = false)
{
    public bool Succeeded => Tokens is not null;
}

/// <summary>
/// Talks to the identity service. Nothing else in this app calls it — sign-in, registration
/// and token refresh all go through here.
/// </summary>
public sealed class AuthApiClient(IHttpClientFactory factory)
{
    private HttpClient Client => factory.CreateClient(HttpClientNames.Auth);

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        try
        {
            var response = await Client.PostAsJsonAsync(
                "/api/v1/auth/login", new { Email = email, Password = password });

            if (response.IsSuccessStatusCode)
                return await ReadTokensAsync(response);

            return new AuthResult(null, await DescribeFailureAsync(response, "Sign-in failed."));
        }
        catch (HttpRequestException)
        {
            return new AuthResult(null, "Could not reach the identity service.");
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        try
        {
            // The service records which version of each document was accepted, so registration
            // has to say. Asking it which versions are current keeps that answer in one place
            // rather than pinning a date in this bundle that quietly goes stale.
            var versions = await Client.GetFromJsonAsync<ConsentVersionsResponse>(
                "/api/v1/auth/consents/versions");

            if (versions is null)
                return new AuthResult(null, "Could not read the current terms from the identity service.");

            var response = await Client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                Email                  = email,
                Password               = password,
                AcceptedTermsVersion   = versions.Terms,
                AcceptedPrivacyVersion = versions.Privacy
            });

            // 202: the account exists but must confirm its address first. Treated as a distinct
            // outcome rather than an error — nothing went wrong, there is simply no token yet.
            if (response.StatusCode == HttpStatusCode.Accepted)
                return new AuthResult(null, null, PendingVerification: true);

            if (response.IsSuccessStatusCode)
                return await ReadTokensAsync(response);

            return new AuthResult(null, await DescribeFailureAsync(response, "Registration failed."));
        }
        catch (HttpRequestException)
        {
            return new AuthResult(null, "Could not reach the identity service.");
        }
    }

    public async Task<TokenResponse?> RefreshAsync(string refreshToken)
    {
        try
        {
            var response = await Client.PostAsJsonAsync(
                "/api/v1/auth/refresh", new { RefreshToken = refreshToken });

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TokenResponse>()
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Revokes the refresh token server-side. Best-effort: signing out locally must succeed
    /// even when the identity service cannot be reached, or a user who is offline is stuck
    /// signed in on a shared machine.
    /// </summary>
    public async Task LogoutAsync(string refreshToken)
    {
        try
        {
            await Client.PostAsJsonAsync("/api/v1/auth/logout", new { RefreshToken = refreshToken });
        }
        catch (HttpRequestException)
        {
            // Deliberately swallowed — see the summary.
        }
    }

    /// <summary>
    /// Reads a 2xx from login or register, which is not always a token pair.
    /// </summary>
    /// <remarks>
    /// An account with two-factor enabled gets 200 with a challenge token instead — deserialising
    /// that as a <see cref="TokenResponse"/> yields an object with a null access token and a
    /// "signed in" state that fails on the next request. This UI does not implement the second
    /// factor, so it says so plainly rather than failing somewhere further along.
    /// </remarks>
    private static async Task<AuthResult> ReadTokensAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        if (document.RootElement.TryGetProperty("requiresTwoFactor", out var twoFactor)
            && twoFactor.ValueKind == JsonValueKind.True)
        {
            return new AuthResult(null,
                "This account has two-factor authentication enabled, which this app does not " +
                "support yet.");
        }

        var tokens = document.Deserialize<TokenResponse>(JsonOptions);

        return string.IsNullOrEmpty(tokens?.AccessToken)
            ? new AuthResult(null, "The identity service returned no access token.")
            : new AuthResult(tokens, null);
    }

    /// <summary>
    /// Turns a failed response into something worth showing a user, without inventing detail.
    /// </summary>
    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, string fallback)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return "Too many attempts. Wait a moment and try again.";

        // Failures come back as {"error": "..."}. The identity service deliberately returns the
        // same generic text whether or not the account exists, so surfacing it leaks nothing —
        // but only the field is shown, never the raw JSON.
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? fallback;
            }
        }
        catch (JsonException)
        {
            // A non-JSON body (a proxy's error page, say) is not worth showing.
        }

        return fallback;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
}
