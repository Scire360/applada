using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Communities.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommunities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communities");

            migrationBuilder.CreateTable(
                name: "groups",
                schema: "communities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                    table.CheckConstraint("ck_groups_member_count", "member_count BETWEEN 0 AND 500");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "communities",
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
                name: "invitations",
                schema: "communities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitations", x => x.id);
                    table.CheckConstraint("ck_invitations_max_uses", "max_uses BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_invitations_use_count", "use_count BETWEEN 0 AND max_uses");
                    table.ForeignKey(
                        name: "fk_invitations_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "communities",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                schema: "communities",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => new { x.group_id, x.user_id });
                    table.CheckConstraint("ck_memberships_role", "role IN ('owner', 'admin', 'member')");
                    table.ForeignKey(
                        name: "fk_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalSchema: "communities",
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitations_group_id",
                schema: "communities",
                table: "invitations",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitations_token_hash",
                schema: "communities",
                table: "invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id_joined_at_group_id",
                schema: "communities",
                table: "memberships",
                columns: new[] { "user_id", "joined_at", "group_id" });

            migrationBuilder.CreateIndex(
                name: "ux_memberships_one_owner",
                schema: "communities",
                table: "memberships",
                column: "group_id",
                unique: true,
                filter: "role = 'owner'");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_next_attempt_at",
                schema: "communities",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "published_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invitations",
                schema: "communities");

            migrationBuilder.DropTable(
                name: "memberships",
                schema: "communities");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "communities");

            migrationBuilder.DropTable(
                name: "groups",
                schema: "communities");
        }
    }
}
