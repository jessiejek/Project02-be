using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalNavigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_prescription_groups_booking_id",
                table: "prescription_groups",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_consultations_doctor_id",
                table: "consultations",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_consultation_diagnoses_consultation_id",
                table: "consultation_diagnoses",
                column: "consultation_id");

            migrationBuilder.AddForeignKey(
                name: "fk_consultation_diagnoses_consultations_consultation_id",
                table: "consultation_diagnoses",
                column: "consultation_id",
                principalTable: "consultations",
                principalColumn: "consultation_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_consultations_doctors_doctor_id",
                table: "consultations",
                column: "doctor_id",
                principalTable: "doctors",
                principalColumn: "doctor_id");

            migrationBuilder.AddForeignKey(
                name: "fk_follow_ups_consultations_consultation_id",
                table: "follow_ups",
                column: "consultation_id",
                principalTable: "consultations",
                principalColumn: "consultation_id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_prescription_groups_bookings_booking_id",
                table: "prescription_groups",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "booking_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_consultation_diagnoses_consultations_consultation_id",
                table: "consultation_diagnoses");

            migrationBuilder.DropForeignKey(
                name: "fk_consultations_doctors_doctor_id",
                table: "consultations");

            migrationBuilder.DropForeignKey(
                name: "fk_follow_ups_consultations_consultation_id",
                table: "follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_prescription_groups_bookings_booking_id",
                table: "prescription_groups");

            migrationBuilder.DropIndex(
                name: "ix_prescription_groups_booking_id",
                table: "prescription_groups");

            migrationBuilder.DropIndex(
                name: "ix_consultations_doctor_id",
                table: "consultations");

            migrationBuilder.DropIndex(
                name: "ix_consultation_diagnoses_consultation_id",
                table: "consultation_diagnoses");
        }
    }
}
