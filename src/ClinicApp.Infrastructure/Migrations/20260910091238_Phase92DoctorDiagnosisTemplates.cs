using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase92DoctorDiagnosisTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "doctor_diagnosis_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_diagnosis_templates", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_diagnosis_templates_doctor_id",
                table: "doctor_diagnosis_templates",
                column: "doctor_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_diagnosis_templates");
        }
    }
}
