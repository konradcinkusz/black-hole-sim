var builder = DistributedApplication.CreateBuilder(args);

// ------------------------------------------------------------------ database
// Postgres in a container; the named volume keeps data across restarts.
// The database resource is named "Default" so the connection string Aspire
// injects lands under ConnectionStrings:Default — exactly the key
// BlackHoleSim.Api already reads (AppDbContext registration in Program.cs),
// so the Api project needs zero changes to run under Aspire.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("blackholesim-pgdata");

var db = postgres.AddDatabase("Default", databaseName: "blackholesim");

// ----------------------------------------------------------------------- api
// Fixed, unproxied port 5080 so it matches BlackHoleSim.Web's dev-time
// ApiBaseUrl (wwwroot/appsettings.Development.json) and the Api's own CORS
// allowlist (Program.cs) — the Blazor WebAssembly client can't do Aspire
// service discovery (it runs in the browser, not in the orchestrated
// process), so both sides need a port they agree on ahead of time.
var api = builder.AddProject<Projects.BlackHoleSim_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    // Readiness, so the dashboard shows the Api as starting while migrations run
    // rather than green-but-500ing — and so WaitFor(api) below means what it says.
    .WithHttpHealthCheck("/health")
    .WithEndpoint("http", e =>
    {
        e.Port = 5080;
        e.TargetPort = 5080;
        e.IsProxied = false;
    });

// ----------------------------------------------------------------------- web
// Same reasoning: fixed, unproxied port 5173 to match the Api's CORS
// allowlist. The Web project doesn't consume WithReference(api) — it has no
// way to receive it (see above) — this just orders startup after the Api.
builder.AddProject<Projects.BlackHoleSim_Web>("web")
    .WaitFor(api)
    .WithEndpoint("http", e =>
    {
        e.Port = 5173;
        e.TargetPort = 5173;
        e.IsProxied = false;
    })
    .WithExternalHttpEndpoints();

builder.Build().Run();
