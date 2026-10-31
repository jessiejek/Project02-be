using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFixesPatientClinicalAndLabValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "gravida",
                table: "patients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardian_name",
                table: "patients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guardian_relationship",
                table: "patients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_pregnant",
                table: "patients",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "last_creatinine_mg_dl",
                table: "patients",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "last_egfr",
                table: "patients",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_renal_labs_date",
                table: "patients",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "lmp_date",
                table: "patients",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "para",
                table: "patients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rh_factor",
                table: "patients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "patient_lab_values",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    test_code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    observed_at = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_lab_values", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_lab_values_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "patient_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_patient_lab_values_patient_id",
                table: "patient_lab_values",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "patient_lab_values");

            migrationBuilder.DropColumn(
                name: "gravida",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "guardian_name",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "guardian_relationship",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "is_pregnant",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "last_creatinine_mg_dl",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "last_egfr",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "last_renal_labs_date",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "lmp_date",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "para",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "rh_factor",
                table: "patients");
        }
    }
}
