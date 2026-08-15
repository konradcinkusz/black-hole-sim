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

            // The gallery's query is "this owner's jobs, newest first", so the index carries
            // the sort column too — otherwise every page costs a sort over the owner's whole
            // history to return twenty rows.
            e.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        });
    }
}
