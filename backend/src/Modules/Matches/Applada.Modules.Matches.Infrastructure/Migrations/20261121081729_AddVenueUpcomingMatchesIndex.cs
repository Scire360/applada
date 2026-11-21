using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Matches.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueUpcomingMatchesIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_matches_venue_upcoming",
                schema: "matches",
                table: "matches",
                columns: new[] { "venue_id", "starts_at", "id" },
                filter: "status = 'scheduled' AND venue_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_matches_venue_upcoming",
                schema: "matches",
                table: "matches");
        }
    }
}
