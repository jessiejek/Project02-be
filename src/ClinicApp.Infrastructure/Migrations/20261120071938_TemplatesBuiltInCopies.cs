using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    //JJQR - AI (Nov 20, 2026) A2 — additive: source_builtin_key on five template tables + doctor_builtin_template_copies
    public partial class TemplatesBuiltInCopies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_builtin_key",
                table: "soap_templates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_builtin_key",
                table: "soap_phrases",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_builtin_key",
                table: "prescription_templates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_builtin_key",
                table: "medical_certificate_templates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_builtin_key",
                table: "doctor_diagnosis_templates",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "doctor_builtin_template_copies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    builtin_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    copied_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_builtin_template_copies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_builtin_template_copies_doctor_id_kind_builtin_key",
                table: "doctor_builtin_template_copies",
                columns: new[] { "doctor_id", "kind", "builtin_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_builtin_template_copies");

            migrationBuilder.DropColumn(
                name: "source_builtin_key",
                table: "soap_templates");

            migrationBuilder.DropColumn(
                name: "source_builtin_key",
                table: "soap_phrases");

            migrationBuilder.DropColumn(
                name: "source_builtin_key",
                table: "prescription_templates");

            migrationBuilder.DropColumn(
                name: "source_builtin_key",
                table: "medical_certificate_templates");

            migrationBuilder.DropColumn(
                name: "source_builtin_key",
                table: "doctor_diagnosis_templates");
        }
    }
}
