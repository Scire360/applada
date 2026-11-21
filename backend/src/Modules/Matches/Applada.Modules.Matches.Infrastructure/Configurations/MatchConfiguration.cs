using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Applada.Modules.Matches.Infrastructure.Configurations;

/// <summary>
/// Tabela <c>matches.matches</c> (spec §8). Os <c>CHECK</c>s repetem os limites de <see cref="MatchLimits"/>:
/// um bug na aplicação aborta a transação em vez de gravar estado inválido (por exemplo, lotar além da capacidade).
/// </summary>
internal sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> match)
    {
        match.ToTable("matches", table =>
        {
            table.HasCheckConstraint("ck_matches_capacity_range", "capacity BETWEEN 1 AND 200");
            table.HasCheckConstraint("ck_matches_duration_range", "duration_minutes BETWEEN 15 AND 600");
            table.HasCheckConstraint("ck_matches_confirmed_within_capacity", "confirmed_count BETWEEN 0 AND capacity");
            table.HasCheckConstraint("ck_matches_waitlist_range", "waitlist_count BETWEEN 0 AND 50");
            table.HasCheckConstraint("ck_matches_status", "status IN ('scheduled', 'cancelled')");
            // Ampliado por migration expansiva quando houver outras visibilidades.
            table.HasCheckConstraint("ck_matches_visibility", "visibility IN ('public')");
        });

        match.HasKey(m => m.Id);
        match.Property(m => m.Id).ValueGeneratedNever();
        match.Property(m => m.OrganizerId).IsRequired();
        match.Property(m => m.GroupId);
        match.Property(m => m.VenueId);
        match.Property(m => m.Title).HasMaxLength(MatchLimits.TitleMax).IsRequired();
        match.Property(m => m.LocationText).HasMaxLength(MatchLimits.LocationTextMax);
        match.Property(m => m.StartsAt).HasColumnType("timestamptz").IsRequired();
        match.Property(m => m.TimeZone).HasColumnType("text").IsRequired();
        match.Property(m => m.DurationMinutes).IsRequired();
        match.Property(m => m.Capacity).IsRequired();
        match.Property(m => m.Visibility).HasConversion(EnumText.MatchVisibilityConverter).HasColumnType("text").IsRequired();
        match.Property(m => m.Status).HasConversion(EnumText.MatchStatusConverter).HasColumnType("text").IsRequired();
        match.Property(m => m.ConfirmedCount).IsRequired();
        match.Property(m => m.WaitlistCount).IsRequired();
        match.Property(m => m.NextQueueSeq).IsRequired();
        // Só edições do organizador incrementam; o EF confere o valor original no UPDATE.
        match.Property(m => m.Version).IsConcurrencyToken().IsRequired();
        match.Property(m => m.CreatedAt).HasColumnType("timestamptz").IsRequired();
        match.Property(m => m.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        match.Property(m => m.CancelledAt).HasColumnType("timestamptz");
        match.Ignore(m => m.DomainEvents);

        match.HasIndex(m => new { m.OrganizerId, m.StartsAt });

        // Próximas partidas de um grupo (só as agendadas), por (starts_at, id).
        match.HasIndex(m => new { m.GroupId, m.StartsAt, m.Id })
            .HasDatabaseName("ix_matches_group_upcoming")
            .HasFilter("status = 'scheduled'");
    }
}
