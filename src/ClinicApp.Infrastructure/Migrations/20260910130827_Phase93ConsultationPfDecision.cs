using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase93ConsultationPfDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "pf_amount",
                table: "consultations",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pf_decision",
                table: "consultations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pf_waive_reason",
                table: "consultations",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pf_amount",
                table: "consultations");

            migrationBuilder.DropColumn(
                name: "pf_decision",
                table: "consultations");

            migrationBuilder.DropColumn(
                name: "pf_waive_reason",
                table: "consultations");
        }
    }
}
