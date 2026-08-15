using System.Net.Http.Json;
using BlackHoleSim.Shared;

namespace BlackHoleSim.Web.Services;

public sealed class RenderApiClient(IHttpClientFactory factory)
{
    private HttpClient Http => factory.CreateClient(HttpClientNames.Api);

    public async Task<RenderJobDto?> SubmitAsync(RenderParameters parameters)
        => await Http.PostAsJsonAsync("/api/render", parameters) is { IsSuccessStatusCode: true } response
            ? await response.Content.ReadFromJsonAsync<RenderJobDto>()
            : null;

    public async Task<RenderJobDto?> GetAsync(Guid jobId)
    {
        var response = await Http.GetAsync($"/api/jobs/{jobId}");

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RenderJobDto>()
            : null;
    }

    public async Task<List<RenderJobDto>> ListAsync(int page = 1, int pageSize = 20)
    {
        var response = await Http.GetAsync($"/api/jobs?page={page}&pageSize={pageSize}");

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<List<RenderJobDto>>() ?? []
            : [];
    }

    public async Task<bool> DeleteAsync(Guid jobId)
    {
        var response = await Http.DeleteAsync($"/api/jobs/{jobId}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Fetches a finished render and returns it as a <c>data:</c> URL for an
    /// <c>&lt;img&gt;</c> or a download link. Null when it is not available to this account.
    /// </summary>
    /// <remarks>
    /// The image used to be a plain URL the browser resolved itself. It cannot be any more:
    /// the browser sets no Authorization header on an <c>&lt;img src&gt;</c> or on a link the
    /// user clicks, so against an authenticated endpoint both fetch a 401 and render a broken
    /// image. Pulling the bytes through this client — which does carry the token — and handing
    /// the page a data URL is what keeps one auth model for every request.
    ///
    /// The cost is that the PNG passes through the WebAssembly heap and grows by a third in
    /// base64. At this app's sizes (a 1920×1080 render is a couple of megabytes) that is
    /// acceptable; a gallery of much larger images would want object URLs and explicit revocation
    /// instead.
    /// </remarks>
    public async Task<string?> GetImageDataUrlAsync(Guid jobId)
    {
        var response = await Http.GetAsync($"/api/jobs/{jobId}/image");
        if (!response.IsSuccessStatusCode) return null;

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
