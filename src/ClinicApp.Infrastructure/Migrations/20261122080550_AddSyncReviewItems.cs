using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncReviewItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_review_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    details_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    other_patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    aggregate = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    entity_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    other_entity_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    origin_node_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    resolution = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    resolution_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_review_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_review_items_kind_entity_id_other_entity_id",
                table: "sync_review_items",
                columns: new[] { "kind", "entity_id", "other_entity_id" },
                unique: true,
                filter: "[entity_id] IS NOT NULL AND [other_entity_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sync_review_items_status_created_at",
                table: "sync_review_items",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_review_items");
        }
    }
}
