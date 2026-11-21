using Applada.BuildingBlocks.Persistence;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Applada.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options), IHasOutbox
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public FormattableString ClaimBatchSql(DateTimeOffset now, int batchSize) => $"""
        SELECT * FROM identity.outbox_messages
        WHERE published_at IS NULL AND next_attempt_at <= {now}
        ORDER BY occurred_at
        LIMIT {batchSize}
        FOR UPDATE SKIP LOCKED
        """;

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

        modelBuilder.AddOutboxMessages();
    }
}
