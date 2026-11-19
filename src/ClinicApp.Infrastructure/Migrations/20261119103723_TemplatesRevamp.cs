using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemplatesRevamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "soap_phrases",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "duration_kind",
                table: "prescription_template_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_value",
                table: "prescription_template_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "indication",
                table: "prescription_template_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meal_relation",
                table: "prescription_template_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "route",
                table: "prescription_template_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "timing",
                table: "prescription_template_items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "doctor_favorite_medicines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "icd10code",
                table: "doctor_diagnosis_templates",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "soap_phrases");

            migrationBuilder.DropColumn(
                name: "duration_kind",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "duration_value",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "indication",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "meal_relation",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "route",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "timing",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "doctor_favorite_medicines");

            migrationBuilder.DropColumn(
                name: "icd10code",
                table: "doctor_diagnosis_templates");
        }
    }
}
