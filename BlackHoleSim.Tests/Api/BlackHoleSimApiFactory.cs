using System.Security.Cryptography;
using BlackHoleSim.Api.Data;
using BlackHoleSim.Api.Jobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace BlackHoleSim.Tests.Api;

/// <summary>
/// Boots the real API in-process, against SQLite and a signing key this test owns.
/// </summary>
/// <remarks>
/// Two substitutions, and both are narrower than they look.
///
/// The database is SQLite rather than Postgres, so the suite still needs nothing installed.
///
/// The signing key is supplied directly instead of being fetched from a JWKS over HTTP, which
/// is the only part of the identity service these tests stand in for. Everything downstream of
/// that is the application's own code running unmodified: the same JwtBearer handler, checking
/// the same signature, issuer, audience and expiry, and the same endpoints deciding what the
/// resulting identity may see. A test that swapped in an "always authenticated" scheme would
/// pass just as happily against an API that had forgotten to validate anything.
/// </remarks>
public sealed class BlackHoleSimApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer   = "BlackHoleSim.Tests";
    public const string Audience = "BlackHoleSim.Tests";

    private readonly RSA _rsa = RSA.Create(2048);

    // Held open for the lifetime of the factory. An in-memory SQLite database exists only
    // while a connection to it is open, so letting EF open and close its own would discard
    // the schema between requests.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RsaSecurityKey SigningKey => new(_rsa) { KeyId = "test-key" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);

        builder.UseSetting("Auth:Authority", "https://identity.invalid");
        builder.UseSetting("Auth:Issuer", Issuer);
        builder.UseSetting("Auth:Audience", Audience);

        builder.ConfigureServices(services =>
        {
            ReplaceDatabase(services);
            RemoveBackgroundServices(services);
            UseLocalSigningKey(services);
        });
    }

    private void ReplaceDatabase(IServiceCollection services)
    {
        // Removing DbContextOptions<AppDbContext> alone is not enough. AddDbContext also
        // registers an options-configuration service carrying the UseNpgsql call, and leaving
        // that behind applies both providers to the same options — which EF rejects at resolve
        // time as two providers in one service provider.
        //
        // Matched by shape rather than by naming the interface: it is not public API, and its
        // namespace has moved between EF versions.
        foreach (var descriptor in services
                     .Where(d => d.ServiceType.FullName?.Contains("DbContextOptions") == true
                              && (d.ServiceType == typeof(DbContextOptions)
                               || d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext))))
                     .ToList())
        {
            services.Remove(descriptor);
        }

        services.RemoveAll<AppDbContext>();

        _connection.Open();

        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));

        using var provider = services.BuildServiceProvider();
        using var scope    = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    /// <summary>
    /// Drops the migration runner and the render worker.
    /// </summary>
    /// <remarks>
    /// The migration service would spend the test run retrying against a Postgres that is not
    /// there, and the worker would start rendering black holes on a background thread — neither
    /// has anything to do with whether an endpoint checks who is calling it.
    /// </remarks>
    private static void RemoveBackgroundServices(IServiceCollection services)
    {
        foreach (var descriptor in services
                     .Where(d => d.ServiceType == typeof(IHostedService))
                     .ToList())
        {
            services.Remove(descriptor);
        }

        // The gate is normally opened by the migration service that was just removed. Nothing
        // reads it without the worker, but leaving it registered keeps the container resolvable.
        services.TryAddSingleton<DatabaseReadyGate>();
        services.TryAddSingleton<JobCancellationRegistry>();
    }

    /// <summary>
    /// Points token validation at this factory's key instead of the identity service's JWKS.
    /// </summary>
    /// <remarks>
    /// Setting <c>Configuration</c> is what keeps the handler off the network: it uses a
    /// configuration that is already present in preference to asking its ConfigurationManager
    /// to fetch one, so no request is made to Auth:Authority.
    /// </remarks>
    private void UseLocalSigningKey(IServiceCollection services)
    {
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration();
            options.Configuration.SigningKeys.Add(SigningKey);

            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        _connection.Dispose();
        _rsa.Dispose();
    }
}
