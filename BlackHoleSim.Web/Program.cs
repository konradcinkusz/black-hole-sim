using BlackHoleSim.Web;
using BlackHoleSim.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Both addresses come from wwwroot/appsettings.json, written into the bundle's directory when
// the container starts — deployment data, so one image is promotable across environments.
//
// In dev:  ApiBaseUrl "http://localhost:5080", AuthBaseUrl "http://localhost:8081"
// In prod: whatever the platform's hostnames are, injected as API_BASE_URL / AUTH_BASE_URL.
//
// An *empty* string is not the same as *missing* config, so "" must fall through to the host's
// own origin too; `??` alone only catches null and left `new Uri("")` to throw
// UriFormatException the moment any page injected a client.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrEmpty(apiBaseUrl))
    apiBaseUrl = builder.HostEnvironment.BaseAddress;

// Unlike the API, the identity service has no same-origin fallback worth guessing at: it is
// always its own app on its own hostname. Missing configuration is a broken deployment, and
// saying so beats posting credentials at this bundle's own origin and getting a 404.
var authBaseUrl = builder.Configuration["AuthBaseUrl"];
if (string.IsNullOrWhiteSpace(authBaseUrl))
{
    throw new InvalidOperationException(
        "AuthBaseUrl is not configured. The web container sets it from AUTH_BASE_URL; for a " +
        "local `dotnet run`, it is in wwwroot/appsettings.Development.json.");
}

// ── Authentication ───────────────────────────────────────────────────────────
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();
builder.Services.AddTransient<BearerTokenHandler>();

// ── HTTP clients ─────────────────────────────────────────────────────────────
// An explicit timeout rather than the standard resilience handler, deliberately.
// AddStandardResilienceHandler brings Polly into the WebAssembly bundle, and its
// retries would be the wrong behaviour here anyway: this client polls a render's
// progress on a timer, so a failed poll is already retried a second later by the
// next poll, and a retrying handler would just stack duplicate in-flight requests
// against a job that is deliberately slow. The server-side kernel does carry the
// full handler — see BlackHoleSim.ServiceDefaults.
//
// 100 seconds is HttpClient's own default; naming it makes it reviewable.
builder.Services.AddHttpClient(HttpClientNames.Api, client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout     = TimeSpan.FromSeconds(100);
}).AddHttpMessageHandler<BearerTokenHandler>();

// No bearer handler on this one. It is where tokens come from, and attaching an expired
// access token to a refresh call would make a failing session unrecoverable.
builder.Services.AddHttpClient(HttpClientNames.Auth, client =>
{
    client.BaseAddress = new Uri(authBaseUrl);
    client.Timeout     = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<RenderApiClient>();

await builder.Build().RunAsync();
