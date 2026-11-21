using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Matches.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance",
                schema: "matches",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance", x => new { x.match_id, x.user_id });
                    table.CheckConstraint("ck_attendance_status", "status IN ('present', 'absent')");
                    table.ForeignKey(
                        name: "fk_attendance_matches_match_id",
                        column: x => x.match_id,
                        principalSchema: "matches",
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance",
                schema: "matches");
        }
    }
}
