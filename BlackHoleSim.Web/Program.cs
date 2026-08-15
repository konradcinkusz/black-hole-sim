using BlackHoleSim.Web;
using BlackHoleSim.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Base URL from wwwroot/appsettings.json → ApiBaseUrl
// In dev: "http://localhost:5080"  (override in appsettings.Development.json)
// In prod: ""  (nginx proxies /api/ to the api container) — an *empty* string
// is not the same as *missing* config, so "" must fall through to the host's
// own origin too; `??` alone only catches null and left `new Uri("")` to throw
// UriFormatException the moment any page injected RenderApiClient in prod.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrEmpty(apiBaseUrl))
    apiBaseUrl = builder.HostEnvironment.BaseAddress;

// An explicit timeout rather than the standard resilience handler, deliberately.
// AddStandardResilienceHandler brings Polly into the WebAssembly bundle, and its
// retries would be the wrong behaviour here anyway: this client polls a render's
// progress on a timer, so a failed poll is already retried a second later by the
// next poll, and a retrying handler would just stack duplicate in-flight requests
// against a job that is deliberately slow. The server-side kernel does carry the
// full handler — see BlackHoleSim.ServiceDefaults.
//
// 100 seconds is HttpClient's own default; naming it makes it reviewable.
builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = new Uri(apiBaseUrl),
        Timeout = TimeSpan.FromSeconds(100)
    });

builder.Services.AddScoped<RenderApiClient>();

await builder.Build().RunAsync();
