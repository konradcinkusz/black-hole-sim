using BlackHoleSim.Shared;
using Microsoft.EntityFrameworkCore;

namespace BlackHoleSim.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<RenderJobEntity> RenderJobs => Set<RenderJobEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<RenderJobEntity>(e =>
        {
            e.HasKey(x => x.Id);

            // Store RenderParameters as a JSON column
            e.OwnsOne(x => x.Parameters, owned =>
            {
                owned.ToJson();
            });

            e.Property(x => x.Status)
             .HasConversion<string>();

            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
