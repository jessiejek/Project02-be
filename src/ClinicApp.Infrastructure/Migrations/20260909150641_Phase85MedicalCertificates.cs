using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase85MedicalCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_certificates",
                columns: table => new
                {
                    certificate_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    patient_address_snapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    examined_at = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    examination_date_from = table.Column<DateOnly>(type: "date", nullable: true),
                    examination_date_to = table.Column<DateOnly>(type: "date", nullable: true),
                    diagnosis_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    recommendations = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    purpose_exception = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    come_back_on = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medical_certificates", x => x.certificate_id);
                    table.ForeignKey(
                        name: "fk_medical_certificates_consultations_consultation_id",
                        column: x => x.consultation_id,
                        principalTable: "consultations",
                        principalColumn: "consultation_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_medical_certificates_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "doctor_id");
                    table.ForeignKey(
                        name: "fk_medical_certificates_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "patient_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_medical_certificates_consultation_id",
                table: "medical_certificates",
                column: "consultation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medical_certificates_doctor_id",
                table: "medical_certificates",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_medical_certificates_patient_id",
                table: "medical_certificates",
                column: "patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_certificates");
        }
    }
}
