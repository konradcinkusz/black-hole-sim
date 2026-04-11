using BlackHoleSim.Web;
using BlackHoleSim.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Base URL from wwwroot/appsettings.json → ApiBaseUrl
// In dev: "http://localhost:5080"  (override in appsettings.Development.json)
// In prod: ""  (nginx proxies /api/ to the api container)
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

builder.Services.AddScoped<RenderApiClient>();

await builder.Build().RunAsync();
