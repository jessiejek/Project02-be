using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientMergedInto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_patient_id",
                table: "patients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_patients_merged_into_patient_id",
                table: "patients",
                column: "merged_into_patient_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_patients_merged_into_patient_id",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "merged_into_patient_id",
                table: "patients");
        }
    }
}
