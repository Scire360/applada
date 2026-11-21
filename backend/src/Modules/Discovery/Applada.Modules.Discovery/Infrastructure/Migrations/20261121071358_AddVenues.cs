using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Applada.Modules.Discovery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVenues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");

            migrationBuilder.EnsureSchema(
                name: "discovery");

            migrationBuilder.CreateTable(
                name: "venues",
                schema: "discovery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    address_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venues", x => x.id);
                    table.CheckConstraint("ck_venues_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_venues_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_venues_name", "char_length(name) >= 3");
                });

            migrationBuilder.Sql("""
                ALTER TABLE discovery.venues ADD COLUMN location geography(Point,4326)
                    GENERATED ALWAYS AS (ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)::geography) STORED;
                CREATE INDEX ix_venues_location ON discovery.venues USING GiST (location);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_venues_created_by_created_at_id",
                schema: "discovery",
                table: "venues",
                columns: new[] { "created_by", "created_at", "id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "venues",
                schema: "discovery");
        }
    }
}
