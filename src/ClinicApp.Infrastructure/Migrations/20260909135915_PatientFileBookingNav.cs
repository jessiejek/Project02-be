using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PatientFileBookingNav : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_patient_lab_results_booking_id",
                table: "patient_lab_results",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_documents_booking_id",
                table: "patient_documents",
                column: "booking_id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_documents_bookings_booking_id",
                table: "patient_documents",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "booking_id");

            migrationBuilder.AddForeignKey(
                name: "fk_patient_lab_results_bookings_booking_id",
                table: "patient_lab_results",
                column: "booking_id",
                principalTable: "bookings",
                principalColumn: "booking_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_patient_documents_bookings_booking_id",
                table: "patient_documents");

            migrationBuilder.DropForeignKey(
                name: "fk_patient_lab_results_bookings_booking_id",
                table: "patient_lab_results");

            migrationBuilder.DropIndex(
                name: "ix_patient_lab_results_booking_id",
                table: "patient_lab_results");

            migrationBuilder.DropIndex(
                name: "ix_patient_documents_booking_id",
                table: "patient_documents");
        }
    }
}
