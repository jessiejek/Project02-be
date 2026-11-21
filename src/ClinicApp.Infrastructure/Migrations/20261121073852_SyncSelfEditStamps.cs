//JJQR - AI (Nov 21, 2026) offline self-edits: sync_field_stamps (per-field last-write time)
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSelfEditStamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_field_stamps",
                columns: table => new
                {
                    aggregate = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    field = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    stamped_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    origin_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_field_stamps", x => new { x.aggregate, x.entity_id, x.field });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_field_stamps");
        }
    }
}
//JJQR - END
