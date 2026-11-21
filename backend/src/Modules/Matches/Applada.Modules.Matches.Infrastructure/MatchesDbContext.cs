using Applada.BuildingBlocks.Persistence;
using Applada.BuildingBlocks.Persistence.Idempotency;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Matches.Domain;
using Applada.Modules.Matches.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Applada.Modules.Matches.Infrastructure;

public sealed class MatchesDbContext(DbContextOptions<MatchesDbContext> options) : DbContext(options), IHasOutbox
{
    public const string Schema = "matches";

    public DbSet<Match> Matches => Set<Match>();

    public DbSet<Registration> Registrations => Set<Registration>();

    internal DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public FormattableString ClaimBatchSql(DateTimeOffset now, int batchSize) => $"""
        SELECT * FROM matches.outbox_messages
        WHERE published_at IS NULL AND next_attempt_at <= {now}
        ORDER BY occurred_at
        LIMIT {batchSize}
        FOR UPDATE SKIP LOCKED
        """;

    /// <summary>
    /// Opções do provedor compartilhadas pela aplicação e pela criação em tempo de design (migrations).
    /// Sem <c>EnableRetryOnFailure</c>: a estratégia de retry não convive com a transação explícita da unidade de trabalho.
    /// </summary>
    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseSnakeCaseNames();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new MatchConfiguration());
        modelBuilder.ApplyConfiguration(new RegistrationConfiguration());
        modelBuilder.ApplyConfiguration(new AttendanceConfiguration());
        modelBuilder.AddIdempotencyRecords();
        modelBuilder.AddOutboxMessages();
    }
}
