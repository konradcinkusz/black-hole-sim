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

// -------------------------------------------------------------- identity service
// This deployment's own instance of konradcinkusz/authservice, pulled as a pinned image
// rather than built — there is no source dependency on that repo, only this reference.
// It shares the Postgres container above but owns its own logical database, which it
// creates on first start.
//
// Fixed port 8081 for the same reason the API and Web have fixed ports: the Blazor
// WebAssembly client signs in against this service from the browser and cannot do Aspire
// service discovery, so both sides need an address agreed ahead of time.
var authKey = Path.Combine(builder.AppHostDirectory, "..", "secrets", "jwt-signing.pem");

if (!File.Exists(authKey))
{
    // Better here, by name, than as an identity service that quietly falls back to HS256,
    // publishes an empty key set, and leaves the API rejecting every token it issues.
    throw new InvalidOperationException(
        $"The token signing key is missing ({Path.GetFullPath(authKey)}). Run ./scripts/setup.sh, " +
        "or generate one with: openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 " +
        "-out secrets/jwt-signing.pem");
}

var auth = builder.AddContainer("auth", "ghcr.io/konradcinkusz/authservice", "v0.3.0")
    .WithHttpEndpoint(port: 8081, targetPort: 8080)
    .WithBindMount(authKey, "/run/secrets/jwt-signing.pem", isReadOnly: true)
    .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
    .WithEnvironment("DatabaseProvider", "PostgreSQL")
    .WithEnvironment("Jwt__PrivateKeyPath", "/run/secrets/jwt-signing.pem")
    // Product-specific rather than the AuthService default: two deployments both on the
    // default issuer/audience would accept each other's tokens.
    .WithEnvironment("Jwt__Issuer", "BlackHoleSim")
    .WithEnvironment("Jwt__Audience", "BlackHoleSim")
    .WithEnvironment("Cors__AllowedOrigins__0", "http://localhost:5173")
    .WithEnvironment(context =>
    {
        // The container reaches Postgres on the container network, not through the host
        // port Aspire publishes, so the connection string is built rather than taken from
        // WithReference — which would hand it a localhost address that resolves, inside
        // the container, to the container itself.
        context.EnvironmentVariables["ConnectionStrings__DefaultConnection"] =
            ReferenceExpression.Create(
                $"Host={postgres.Resource.Name};Port=5432;Database=authservice;" +
                $"Username=postgres;Password={postgres.Resource.PasswordParameter}");
    })
    .WaitFor(db);

// ----------------------------------------------------------------------- api
// Fixed, unproxied port 5080 so it matches BlackHoleSim.Web's dev-time
// ApiBaseUrl (wwwroot/appsettings.Development.json) and the Api's own CORS
// allowlist (Program.cs) — the Blazor WebAssembly client can't do Aspire
// service discovery (it runs in the browser, not in the orchestrated
// process), so both sides need a port they agree on ahead of time.
var api = builder.AddProject<Projects.BlackHoleSim_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    // Where to fetch the signing keys. The API runs as a host process here, not in the
    // container network, so it reaches the identity service on the published port.
    //
    // Deliberately not WaitFor(auth): the API discovers keys lazily, on the first request
    // carrying a token, so it has no reason to block startup on the identity service.
    .WithEnvironment("Auth__Authority", "http://localhost:8081")
    .WithEnvironment("Auth__Issuer", "BlackHoleSim")
    .WithEnvironment("Auth__Audience", "BlackHoleSim")
    .WithEnvironment("Auth__RequireHttpsMetadata", "false")
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
