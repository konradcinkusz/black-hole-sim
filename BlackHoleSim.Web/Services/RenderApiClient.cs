using System.Net.Http.Json;
using BlackHoleSim.Shared;

namespace BlackHoleSim.Web.Services;

public sealed class RenderApiClient(HttpClient http)
{
    public async Task<RenderJobDto?> SubmitAsync(RenderParameters parameters)
        => await http.PostAsJsonAsync("/api/render", parameters) is { IsSuccessStatusCode: true } response
            ? await response.Content.ReadFromJsonAsync<RenderJobDto>()
            : null;

    public async Task<RenderJobDto?> GetAsync(Guid jobId)
        => await http.GetFromJsonAsync<RenderJobDto>($"/api/jobs/{jobId}");

    public async Task<List<RenderJobDto>> ListAsync(int page = 1, int pageSize = 20)
        => await http.GetFromJsonAsync<List<RenderJobDto>>(
            $"/api/jobs?page={page}&pageSize={pageSize}") ?? [];

    public async Task<bool> DeleteAsync(Guid jobId)
    {
        var response = await http.DeleteAsync($"/api/jobs/{jobId}");
        return response.IsSuccessStatusCode;
    }

    // Absolute, not relative: an <img>/<a> src is resolved by the browser
    // against the *page's* origin, not through this HttpClient. That's fine
    // in prod (Web and Api share an origin via nginx) but breaks whenever the
    // Web dev server and the Api run on different ports — a relative path
    // silently 200s off the Blazor WASM dev server's own SPA fallback
    // (index.html) instead of hitting the Api, so the image never loads.
    public string GetImageUrl(Guid jobId) => new Uri(http.BaseAddress!, $"api/jobs/{jobId}/image").ToString();
}
