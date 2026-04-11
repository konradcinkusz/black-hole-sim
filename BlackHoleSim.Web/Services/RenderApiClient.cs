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

    public string GetImageUrl(Guid jobId) => $"/api/jobs/{jobId}/image";
}
