using Applada.BuildingBlocks.Persistence;
using Applada.BuildingBlocks.Persistence.Outbox;
using Applada.Modules.Communities.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Applada.Modules.Communities.Infrastructure;

public sealed class CommunitiesDbContext(DbContextOptions<CommunitiesDbContext> options) : DbContext(options), IHasOutbox
{
    public const string Schema = "communities";

    internal DbSet<Group> Groups => Set<Group>();

    internal DbSet<Membership> Memberships => Set<Membership>();

    internal DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public FormattableString ClaimBatchSql(DateTimeOffset now, int batchSize) => $"""
        SELECT * FROM communities.outbox_messages
        WHERE published_at IS NULL AND next_attempt_at <= {now}
        ORDER BY occurred_at
        LIMIT {batchSize}
        FOR UPDATE SKIP LOCKED
        """;

    internal static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseSnakeCaseNames();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Group>(group =>
        {
            group.ToTable("groups", t =>
                t.HasCheckConstraint("ck_groups_member_count", $"member_count BETWEEN 0 AND {GroupRules.MaxMembers}"));
            group.HasKey(g => g.Id);
            group.Property(g => g.Id).ValueGeneratedNever();
            group.Property(g => g.Name).HasMaxLength(GroupRules.NameMaxLength).IsRequired();
            group.Property(g => g.Description).HasMaxLength(GroupRules.DescriptionMaxLength);
            group.Property(g => g.CreatedAt).HasColumnType("timestamptz").IsRequired();
            group.Property(g => g.MemberCount).IsRequired();
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships", t =>
                t.HasCheckConstraint("ck_memberships_role", "role IN ('owner', 'admin', 'member')"));
            membership.HasKey(m => new { m.GroupId, m.UserId });
            membership.Property(m => m.Role)
                .HasConversion(role => GroupRules.ToText(role), text => GroupRules.FromText(text))
                .HasColumnType("text")
                .IsRequired();
            membership.Property(m => m.JoinedAt).HasColumnType("timestamptz").IsRequired();
            membership.HasOne<Group>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
            membership.HasIndex(m => m.GroupId).IsUnique().HasFilter("role = 'owner'").HasDatabaseName("ux_memberships_one_owner");
            membership.HasIndex(m => new { m.UserId, m.JoinedAt, m.GroupId });
        });

        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("invitations", t =>
            {
                t.HasCheckConstraint("ck_invitations_max_uses", $"max_uses BETWEEN 1 AND {GroupRules.MaxInvitationUses}");
                t.HasCheckConstraint("ck_invitations_use_count", "use_count BETWEEN 0 AND max_uses");
            });
            invitation.HasKey(i => i.Id);
            invitation.Property(i => i.Id).ValueGeneratedNever();
            invitation.Property(i => i.TokenHash).HasColumnType("bytea").IsRequired();
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.Property(i => i.CreatedAt).HasColumnType("timestamptz").IsRequired();
            invitation.Property(i => i.ExpiresAt).HasColumnType("timestamptz").IsRequired();
            invitation.Property(i => i.RevokedAt).HasColumnType("timestamptz");
            invitation.HasOne<Group>().WithMany().HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.AddOutboxMessages();
    }
}
