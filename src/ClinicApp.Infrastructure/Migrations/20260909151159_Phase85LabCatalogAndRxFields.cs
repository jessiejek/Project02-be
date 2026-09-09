using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase85LabCatalogAndRxFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "duration_kind",
                table: "prescription_line_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_value",
                table: "prescription_line_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "indication",
                table: "prescription_line_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meal_relation",
                table: "prescription_line_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "timing",
                table: "prescription_line_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lab_test_id",
                table: "lab_orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lab_test_catalog",
                columns: table => new
                {
                    lab_test_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_test_catalog", x => x.lab_test_id);
                });

            migrationBuilder.InsertData(
                table: "lab_test_catalog",
                columns: new[] { "lab_test_id", "created_at", "is_default", "name", "sort_order" },
                values: new object[,]
                {
                    { new Guid("33333333-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "CBC", 1 },
                    { new Guid("33333333-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "URINALYSIS", 2 },
                    { new Guid("33333333-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "LIPID PROFILE", 3 },
                    { new Guid("33333333-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "SGPT", 4 },
                    { new Guid("33333333-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "CREA", 5 },
                    { new Guid("33333333-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "FBS", 6 },
                    { new Guid("33333333-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "BUA", 7 },
                    { new Guid("33333333-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "DENGUE NS1/IgG/IgM", 8 },
                    { new Guid("33333333-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "ECG 12L", 9 },
                    { new Guid("33333333-0000-0000-0000-000000000010"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "TSH", 10 },
                    { new Guid("33333333-0000-0000-0000-000000000011"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "FT3", 11 },
                    { new Guid("33333333-0000-0000-0000-000000000012"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "FT4", 12 },
                    { new Guid("33333333-0000-0000-0000-000000000013"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "HbA1c", 13 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_lab_orders_lab_test_id",
                table: "lab_orders",
                column: "lab_test_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_test_catalog_name",
                table: "lab_test_catalog",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_lab_orders_lab_test_catalog_lab_test_id",
                table: "lab_orders",
                column: "lab_test_id",
                principalTable: "lab_test_catalog",
                principalColumn: "lab_test_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lab_orders_lab_test_catalog_lab_test_id",
                table: "lab_orders");

            migrationBuilder.DropTable(
                name: "lab_test_catalog");

            migrationBuilder.DropIndex(
                name: "ix_lab_orders_lab_test_id",
                table: "lab_orders");

            migrationBuilder.DropColumn(
                name: "duration_kind",
                table: "prescription_line_items");

            migrationBuilder.DropColumn(
                name: "duration_value",
                table: "prescription_line_items");

            migrationBuilder.DropColumn(
                name: "indication",
                table: "prescription_line_items");

            migrationBuilder.DropColumn(
                name: "meal_relation",
                table: "prescription_line_items");

            migrationBuilder.DropColumn(
                name: "timing",
                table: "prescription_line_items");

            migrationBuilder.DropColumn(
                name: "lab_test_id",
                table: "lab_orders");
        }
    }
}
