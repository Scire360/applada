using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Discovery.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Applada.Modules.Discovery.Infrastructure;

public sealed class DiscoveryDbContext(DbContextOptions<DiscoveryDbContext> options) : DbContext(options)
{
    private const string Schema = "discovery";

    internal DbSet<Venue> Venues => Set<Venue>();

    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseSnakeCaseNames();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Venue>(venue =>
        {
            venue.ToTable("venues", table =>
            {
                table.HasCheckConstraint("ck_venues_latitude", "latitude BETWEEN -90 AND 90");
                table.HasCheckConstraint("ck_venues_longitude", "longitude BETWEEN -180 AND 180");
                table.HasCheckConstraint("ck_venues_name", $"char_length(name) >= {Venue.MinNameLength}");
            });
            venue.HasKey(v => v.Id);
            venue.Property(v => v.Id).ValueGeneratedNever();
            venue.Property(v => v.Name).HasMaxLength(Venue.MaxNameLength).IsRequired();
            venue.Property(v => v.AddressText).HasMaxLength(Venue.MaxAddressLength);
            venue.Property(v => v.CreatedAt).HasColumnType("timestamptz");
            venue.HasIndex(v => new { v.CreatedBy, v.CreatedAt, v.Id }).IsDescending(false, true, true);
            // Coluna geography gerada pelo PostGIS na migration, fora do modelo EF.
            venue.Ignore("location");
        });
    }
}
