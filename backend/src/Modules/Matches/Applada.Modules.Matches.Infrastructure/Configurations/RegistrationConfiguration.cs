using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Applada.Modules.Matches.Infrastructure.Configurations;

/// <summary>Tabela <c>matches.registrations</c> (spec §8): uma linha por usuário e partida, reaproveitada na reinscrição.</summary>
internal sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> registration)
    {
        registration.ToTable("registrations", table =>
            table.HasCheckConstraint("ck_registrations_status", "status IN ('confirmed', 'waitlisted', 'withdrawn')"));

        registration.HasKey(r => r.Id);
        registration.Property(r => r.Id).ValueGeneratedNever();
        registration.Property(r => r.MatchId).IsRequired();
        registration.Property(r => r.UserId).IsRequired();
        registration.Property(r => r.Status).HasConversion(EnumText.RegistrationStatusConverter).HasColumnType("text").IsRequired();
        registration.Property(r => r.QueueSeq).IsRequired();
        registration.Property(r => r.CreatedAt).HasColumnType("timestamptz").IsRequired();
        registration.Property(r => r.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        registration.HasOne<Match>().WithMany().HasForeignKey(r => r.MatchId).OnDelete(DeleteBehavior.Restrict);

        registration.HasIndex(r => new { r.MatchId, r.UserId }).IsUnique();
        // Promoção da espera e listagem de participantes.
        registration.HasIndex(r => new { r.MatchId, r.Status, r.QueueSeq });
        // "Minhas partidas" como participante.
        registration.HasIndex(r => new { r.UserId, r.Status });
    }
}
