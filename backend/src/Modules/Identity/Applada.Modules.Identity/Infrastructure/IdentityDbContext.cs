using Applada.BuildingBlocks.Persistence;
using Applada.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Applada.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Opções do provedor compartilhadas pela aplicação e pela criação em tempo de design (migrations).
    /// </summary>
    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseSnakeCaseNames();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(user =>
        {
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.Issuer).HasColumnType("text").IsRequired();
            user.Property(u => u.Subject).HasColumnType("text").IsRequired();
            user.Property(u => u.CreatedAt).HasColumnType("timestamptz").IsRequired();
            user.HasIndex(u => new { u.Issuer, u.Subject }).IsUnique();
        });
    }
}
