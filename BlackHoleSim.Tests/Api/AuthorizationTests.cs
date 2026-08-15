using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlackHoleSim.Api.Data;
using BlackHoleSim.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlackHoleSim.Tests.Api;

/// <summary>
/// What the job endpoints will and will not do for a given caller.
/// </summary>
public sealed class AuthorizationTests : IClassFixture<BlackHoleSimApiFactory>, IDisposable
{
    private const string Alice = "user-alice";
    private const string Bob   = "user-bob";

    private readonly BlackHoleSimApiFactory _factory;

    public AuthorizationTests(BlackHoleSimApiFactory factory) => _factory = factory;

    // ── Nothing without a token ──────────────────────────────────────────────

    [Theory]
    [InlineData("GET",    "/api/jobs")]
    [InlineData("GET",    "/api/jobs/8a3d0f4e-0000-0000-0000-000000000000")]
    [InlineData("GET",    "/api/jobs/8a3d0f4e-0000-0000-0000-000000000000/image")]
    [InlineData("DELETE", "/api/jobs/8a3d0f4e-0000-0000-0000-000000000000")]
    [InlineData("POST",   "/api/render")]
    public async Task Refuses_an_anonymous_caller(string method, string path)
    {
        using var client   = _factory.CreateClient();
        using var request  = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refuses_an_expired_token()
    {
        using var client = CreateClient(TestTokens.Expired(_factory, Alice));

        var response = await client.GetAsync("/api/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refuses_a_token_signed_by_someone_else()
    {
        // The shape is right and the claims are right; only the key is wrong. This is the
        // case that would pass anyway if the API were configured to skip signature
        // validation, or handed a symmetric secret it could also sign with.
        using var other  = new BlackHoleSimApiFactory();
        using var client = CreateClient(TestTokens.For(other, Alice));

        var response = await client.GetAsync("/api/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_stays_open()
    {
        // Whatever else changes, a probe must not need credentials — a health endpoint behind
        // authentication reports the platform's unhealthy exactly when the identity service is.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── A signed-in caller sees only their own work ──────────────────────────

    [Fact]
    public async Task Lists_only_the_callers_own_jobs()
    {
        var mine     = await GivenAJob(Alice);
        var theirs   = await GivenAJob(Bob);

        using var client = CreateClient(TestTokens.For(_factory, Alice));

        var jobs = await client.GetFromJsonAsync<List<RenderJobDto>>("/api/jobs");

        jobs.Should().NotBeNull();
        jobs!.Select(j => j.Id).Should().Contain(mine).And.NotContain(theirs);
    }

    [Fact]
    public async Task Answers_404_for_someone_elses_job()
    {
        var theirs = await GivenAJob(Bob);

        using var client = CreateClient(TestTokens.For(_factory, Alice));

        var response = await client.GetAsync($"/api/jobs/{theirs}");

        // Not 403. 403 would confirm the id names a real render, which is exactly the
        // enumeration answer the ownership filter exists to withhold.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Will_not_delete_someone_elses_job()
    {
        var theirs = await GivenAJob(Bob);

        using var client = CreateClient(TestTokens.For(_factory, Alice));

        var response = await client.DeleteAsync($"/api/jobs/{theirs}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The status code is only half the claim. Before ownership was enforced this
        // endpoint answered 204 and the row was gone.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.RenderJobs.FindAsync(theirs)).Should().NotBeNull();
    }

    [Fact]
    public async Task Will_not_fetch_someone_elses_image()
    {
        var theirs = await GivenAJob(Bob, completedWith: [1, 2, 3, 4]);

        using var client = CreateClient(TestTokens.For(_factory, Alice));

        var response = await client.GetAsync($"/api/jobs/{theirs}/image");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Files_a_submitted_render_under_the_caller()
    {
        using var client = CreateClient(TestTokens.For(_factory, Alice, "alice@example.com"));

        var response = await client.PostAsJsonAsync("/api/render", new RenderParameters());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var created = await response.Content.ReadFromJsonAsync<RenderJobDto>();
        created.Should().NotBeNull();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stored = await db.RenderJobs.FindAsync(created!.Id);
        stored.Should().NotBeNull();
        stored!.OwnerId.Should().Be(Alice);
    }

    [Fact]
    public async Task Hides_jobs_that_predate_authentication()
    {
        // Rows written before renders had owners. They belong to nobody, so they are nobody's
        // to read — the alternative, showing them to everyone, is the behaviour being removed.
        var orphan = await GivenAJob(owner: null);

        using var client = CreateClient(TestTokens.For(_factory, Alice));

        var jobs = await client.GetFromJsonAsync<List<RenderJobDto>>("/api/jobs");
        jobs!.Select(j => j.Id).Should().NotContain(orphan);

        (await client.GetAsync($"/api/jobs/{orphan}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private HttpClient CreateClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> GivenAJob(string? owner, byte[]? completedWith = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = new RenderJobEntity
        {
            Id         = Guid.NewGuid(),
            OwnerId    = owner,
            Parameters = new RenderParameters(),
            Status     = completedWith is null ? RenderJobStatus.Pending : RenderJobStatus.Completed,
            Png        = completedWith,
            CreatedAt  = DateTime.UtcNow
        };

        db.RenderJobs.Add(entity);
        await db.SaveChangesAsync();

        return entity.Id;
    }

    public void Dispose()
    {
        // The factory is shared across every test in this class, so rows from one must not be
        // visible to the next.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RenderJobs.RemoveRange(db.RenderJobs);
        db.SaveChanges();
    }
}
