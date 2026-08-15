using System.Net;
using System.Net.Http.Headers;

namespace BlackHoleSim.Web.Services;

/// <summary>
/// Attaches the access token to every call to BlackHoleSim.Api, and refreshes it once when the
/// API says the token is no longer good.
/// </summary>
public sealed class BearerTokenHandler(TokenStore tokens, IServiceProvider services) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAccessTokenAsync();

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized || string.IsNullOrEmpty(token))
            return response;

        // One retry, and only after a *successful* refresh. Looping here would turn an expired
        // session into a burst of sign-in attempts against the identity service's rate limiter.
        //
        // AuthSession is resolved lazily rather than injected: it depends on the authentication
        // state provider, which the HttpClient factory would otherwise have to construct while
        // it is still building this handler.
        var refreshed = await services.GetRequiredService<AuthSession>().TryRefreshAsync(token);
        if (string.IsNullOrEmpty(refreshed)) return response;

        response.Dispose();

        var retry = await CloneAsync(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);

        return await base.SendAsync(retry, cancellationToken);
    }

    /// <summary>
    /// A sent request cannot be sent again, so the retry needs a copy — including the body,
    /// which is why the content is buffered rather than re-read from a spent stream.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version
        };

        if (request.Content is not null)
        {
            var body = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(body);

            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in (IDictionary<string, object?>)request.Options)
            clone.Options.TryAdd(option.Key, option.Value);

        return clone;
    }
}
