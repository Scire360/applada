using Applada.Modules.Matches.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Applada.Modules.Matches.Infrastructure.Configurations;

internal sealed record AttendanceRecord(Guid MatchId, Guid UserId, string Status, Guid RecordedBy, DateTimeOffset RecordedAt);

internal sealed class AttendanceConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> attendance)
    {
        attendance.ToTable("attendance", table =>
            table.HasCheckConstraint("ck_attendance_status", "status IN ('present', 'absent')"));
        attendance.HasKey(a => new { a.MatchId, a.UserId });
        attendance.Property(a => a.Status).HasColumnType("text").IsRequired();
        attendance.Property(a => a.RecordedAt).HasColumnType("timestamptz");
        attendance.HasOne<Match>().WithMany().HasForeignKey(a => a.MatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
