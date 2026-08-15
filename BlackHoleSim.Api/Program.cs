using System.Threading.RateLimiting;
using BlackHoleSim.Api.Auth;
using BlackHoleSim.Api.Data;
using BlackHoleSim.Api.Endpoints;
using BlackHoleSim.Api.Jobs;
using BlackHoleSim.ServiceDefaults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

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

// ── Authentication ────────────────────────────────────────────────────────────
// Tokens are minted by the identity service this deployment runs (konradcinkusz/
// authservice) and only verified here, against the public keys published at its JWKS.
// This service holds no signing key and cannot issue a token for anyone.
builder.AddBlackHoleSimAuthentication();

// ── Job queue & background worker ─────────────────────────────────────────────
builder.Services.AddSingleton<IRenderJobQueue, ChannelRenderJobQueue>();
builder.Services.AddSingleton<JobCancellationRegistry>();
builder.Services.AddHostedService<RenderWorker>();

// ── Rate limiting ─────────────────────────────────────────────────────────────
// Partitioned by caller, not global. AddFixedWindowLimiter builds a single window shared
// by everyone, so five renders a minute was five for the whole deployment: one enthusiastic
// client starved every other, and the limit said nothing about what any one account could
// consume. Now that a request carries an identity, the window is per user — which is the
// unit the limit was always meant to describe.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("render", http => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: http.User.Identity?.IsAuthenticated == true
            ? http.User.OwnerId()
            // Unreachable while the endpoint requires authorization (that middleware runs
            // first and short-circuits), but a limiter that silently shares one partition
            // across every anonymous caller is not the thing to leave behind if it ever is.
            : $"anon:{http.Connection.RemoteIpAddress}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit          = 5,
            Window               = TimeSpan.FromMinutes(1),
            QueueLimit           = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));

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

        // Every job endpoint needs a bearer token now, so the explorer is unusable without
        // somewhere to paste one. Swagger cannot mint it: obtain a token from the identity
        // service (POST /api/v1/auth/login) and paste the accessToken here.
        c.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
        {
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            In           = ParameterLocation.Header,
            Description  = "Access token issued by the identity service."
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "bearer"
                }
            }] = []
        });
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

// Order matters twice over. Routing first, so the rate limiter can see which endpoint (and
// therefore which policy) a request resolved to. Authentication and authorization before the
// limiter, so the limiter's partition key is a real user id rather than whatever the request
// claimed, and so an unauthenticated flood is refused before it consumes anyone's window.
app.UseRouting();
app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapDefaultEndpoints();   // /health (readiness) and /alive (liveness)
app.MapRenderEndpoints();
app.MapJobsEndpoints();
app.MapHealthEndpoints();    // the /api/health aliases this service kept

app.Run();

/// <summary>
/// Exposed so the test project can boot this application through
/// <c>WebApplicationFactory&lt;Program&gt;</c>. A top-level program's generated class is
/// internal, and the authorization tests are worth more than the privacy of a type that has
/// no members.
/// </summary>
public partial class Program;
