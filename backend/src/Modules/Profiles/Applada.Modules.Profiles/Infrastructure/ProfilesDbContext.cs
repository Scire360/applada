using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Profiles.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Applada.Modules.Profiles.Infrastructure;

public sealed class ProfilesDbContext(DbContextOptions<ProfilesDbContext> options) : DbContext(options)
{
    public const string Schema = "profiles";

    internal DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();

    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseSnakeCaseNames();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<PlayerProfile>(profile =>
        {
            profile.ToTable("player_profiles");
            profile.HasKey(p => p.UserId);
            profile.Property(p => p.UserId).ValueGeneratedNever();
            profile.Property(p => p.DisplayName).HasMaxLength(DisplayNameRules.MaxLength).IsRequired();
        });
    }
}
