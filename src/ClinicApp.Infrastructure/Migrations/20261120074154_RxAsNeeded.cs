using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RxAsNeeded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "as_needed",
                table: "prescription_template_items",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "as_needed",
                table: "prescription_line_items",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "as_needed",
                table: "prescription_template_items");

            migrationBuilder.DropColumn(
                name: "as_needed",
                table: "prescription_line_items");
        }
    }
}
