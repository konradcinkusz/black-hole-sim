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

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

builder.Services.AddScoped<RenderApiClient>();

await builder.Build().RunAsync();
