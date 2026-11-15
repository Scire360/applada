using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Matches.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniqueRegistrationQueueSeq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_registrations_match_id_queue_seq",
                schema: "matches",
                table: "registrations",
                columns: new[] { "match_id", "queue_seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_registrations_match_id_queue_seq",
                schema: "matches",
                table: "registrations");
        }
    }
}
