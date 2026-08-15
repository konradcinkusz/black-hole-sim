using System.Threading.RateLimiting;
using BlackHoleSim.Api.Data;
using BlackHoleSim.Api.Endpoints;
using BlackHoleSim.Api.Jobs;
using BlackHoleSim.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

const string CorsPolicy = "frontend";

// ── Shared kernel ─────────────────────────────────────────────────────────────
// Telemetry, the "self" liveness check, service discovery and resilient HTTP
// defaults. Everything below this line is specific to *this* service.
builder.AddServiceDefaults();

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(
        builder.Configuration.GetConnectionString("Default"),
        npgsql => npgsql.EnableRetryOnFailure()));

// ── Schema readiness ──────────────────────────────────────────────────────────
// Migrations run after the listener is up (see DatabaseMigrationService); the gate
// keeps RenderWorker off the tables until they exist.
builder.Services.AddSingleton<DatabaseReadyGate>();
builder.Services.AddHostedService<DatabaseMigrationService>();

// ── Job queue & background worker ─────────────────────────────────────────────
builder.Services.AddSingleton<IRenderJobQueue, ChannelRenderJobQueue>();
builder.Services.AddSingleton<JobCancellationRegistry>();
builder.Services.AddHostedService<RenderWorker>();

// ── Rate limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("render", opt =>
    {
        opt.PermitLimit    = 5;
        opt.Window         = TimeSpan.FromMinutes(1);
        opt.QueueLimit     = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Health checks ─────────────────────────────────────────────────────────────
// The live-tagged "self" check comes from AddServiceDefaults. These two are this
// service's own, and neither is live-tagged on purpose: a database that has gone
// away is a readiness problem, and restarting the process would not fix it.
builder.Services.AddHealthChecks()
    .AddCheck<SchemaReadyHealthCheck>("schema")
    .AddDbContextCheck<AppDbContext>("db");

// ── CORS ──────────────────────────────────────────────────────────────────────
// The browser talks to this API directly across an origin boundary (the frontend is
// a separate app with its own hostname), so the allowlist is deployment data, not a
// constant: Cors__AllowedOrigins__0, __1 … in the environment. Defaults cover the
// local Aspire/dev ports so a fresh clone still works with nothing set.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>();

if (allowedOrigins is null or { Length: 0 })
    allowedOrigins = ["http://localhost:5173", "http://localhost:5080"];

builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
    p.WithOrigins(allowedOrigins)
     .AllowAnyHeader()
     .AllowAnyMethod()));

// ── OpenAPI / Swagger (dev only) ──────────────────────────────────────────────
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new() { Title = "BlackHoleSim API", Version = "v1" });
    });
}

// ── Request size limit ────────────────────────────────────────────────────────
builder.WebHost.ConfigureKestrel(o =>
    o.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.UseRateLimiter();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapDefaultEndpoints();   // /health (readiness) and /alive (liveness)
app.MapRenderEndpoints();
app.MapJobsEndpoints();
app.MapHealthEndpoints();    // the /api/health aliases this service kept

app.Run();
