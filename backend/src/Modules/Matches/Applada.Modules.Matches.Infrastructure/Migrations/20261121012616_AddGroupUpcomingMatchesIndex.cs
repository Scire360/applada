using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Matches.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupUpcomingMatchesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_matches_group_upcoming",
                schema: "matches",
                table: "matches",
                columns: new[] { "group_id", "starts_at", "id" },
                filter: "status = 'scheduled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_matches_group_upcoming",
                schema: "matches",
                table: "matches");
        }
    }
}
