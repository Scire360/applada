using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Matches.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matches");

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "matches",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.user_id, x.key });
                });

            migrationBuilder.CreateTable(
                name: "matches",
                schema: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    confirmed_count = table.Column<int>(type: "integer", nullable: false),
                    waitlist_count = table.Column<int>(type: "integer", nullable: false),
                    next_queue_seq = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                    table.CheckConstraint("ck_matches_capacity_range", "capacity BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_matches_confirmed_within_capacity", "confirmed_count BETWEEN 0 AND capacity");
                    table.CheckConstraint("ck_matches_duration_range", "duration_minutes BETWEEN 15 AND 600");
                    table.CheckConstraint("ck_matches_status", "status IN ('scheduled', 'cancelled')");
                    table.CheckConstraint("ck_matches_visibility", "visibility IN ('public')");
                    table.CheckConstraint("ck_matches_waitlist_range", "waitlist_count BETWEEN 0 AND 50");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    queue_seq = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                    table.CheckConstraint("ck_registrations_status", "status IN ('confirmed', 'waitlisted', 'withdrawn')");
                    table.ForeignKey(
                        name: "fk_registrations_matches_match_id",
                        column: x => x.match_id,
                        principalSchema: "matches",
                        principalTable: "matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_created_at",
                schema: "matches",
                table: "idempotency_keys",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_matches_organizer_id_starts_at",
                schema: "matches",
                table: "matches",
                columns: new[] { "organizer_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_next_attempt_at",
                schema: "matches",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_match_id_status_queue_seq",
                schema: "matches",
                table: "registrations",
                columns: new[] { "match_id", "status", "queue_seq" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_match_id_user_id",
                schema: "matches",
                table: "registrations",
                columns: new[] { "match_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_registrations_user_id_status",
                schema: "matches",
                table: "registrations",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "registrations",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "matches",
                schema: "matches");
        }
    }
}
