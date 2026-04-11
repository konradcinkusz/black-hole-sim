using System.Threading.RateLimiting;
using BlackHoleSim.Api.Data;
using BlackHoleSim.Api.Endpoints;
using BlackHoleSim.Api.Jobs;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("db");

// ── CORS (dev only) ───────────────────────────────────────────────────────────
builder.Services.AddCors(o => o.AddPolicy("dev", p =>
    p.WithOrigins("http://localhost:5173", "http://localhost:5080")
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
    app.UseCors("dev");

    // Auto-migrate in dev
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseRateLimiter();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapRenderEndpoints();
app.MapJobsEndpoints();
app.MapHealthEndpoints();

app.Run();
