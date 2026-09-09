using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase8FeeScheduleAndVisitType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pwd_id_number",
                table: "patients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "senior_id_number",
                table: "patients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_pct",
                table: "clinic_settings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_consultation",
                table: "clinic_settings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_follow_up",
                table: "clinic_settings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_med_cert",
                table: "clinic_settings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "fee_senior_pwd",
                table: "clinic_settings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "bookings",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_category",
                table: "bookings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "med_cert_requested",
                table: "bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "visit_type",
                table: "bookings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "New");

            migrationBuilder.UpdateData(
                table: "clinic_operating_hours",
                keyColumn: "day_of_week",
                keyValue: (short)6,
                columns: new[] { "close_time", "open_time" },
                values: new object[] { new TimeOnly(17, 0, 0), new TimeOnly(10, 0, 0) });

            migrationBuilder.UpdateData(
                table: "clinic_settings",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "address", "clinic_name", "contact_number", "discount_pct", "fee_consultation", "fee_follow_up", "fee_med_cert", "fee_senior_pwd" },
                values: new object[] { "3ML Quezon National Highway, Buaya, Lapu-Lapu City", "Grace Medical Clinic", "09285612976", 0.20m, 450m, 350m, 50m, 400m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "pwd_id_number",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "senior_id_number",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "discount_pct",
                table: "clinic_settings");

            migrationBuilder.DropColumn(
                name: "fee_consultation",
                table: "clinic_settings");

            migrationBuilder.DropColumn(
                name: "fee_follow_up",
                table: "clinic_settings");

            migrationBuilder.DropColumn(
                name: "fee_med_cert",
                table: "clinic_settings");

            migrationBuilder.DropColumn(
                name: "fee_senior_pwd",
                table: "clinic_settings");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "discount_category",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "med_cert_requested",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "visit_type",
                table: "bookings");

            migrationBuilder.UpdateData(
                table: "clinic_operating_hours",
                keyColumn: "day_of_week",
                keyValue: (short)6,
                columns: new[] { "close_time", "open_time" },
                values: new object[] { new TimeOnly(12, 0, 0), new TimeOnly(8, 0, 0) });

            migrationBuilder.UpdateData(
                table: "clinic_settings",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "address", "clinic_name", "contact_number" },
                values: new object[] { "TBD", "Dr. Grace Gavino Medical Clinic", null });
        }
    }
}
