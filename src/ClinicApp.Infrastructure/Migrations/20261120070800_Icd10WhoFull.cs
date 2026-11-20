using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    //JJQR - AI (Nov 20, 2026) B1/B3 — additive: AddColumn level/is_leaf, CreateIndex, DeleteData of 5 old HasData rows; AlterColumn description to nvarchar(450) so the index is legal on SQL Server
    public partial class Icd10WhoFull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "icd10_codes",
                keyColumn: "code",
                keyValue: "E11.9");

            migrationBuilder.DeleteData(
                table: "icd10_codes",
                keyColumn: "code",
                keyValue: "I10");

            migrationBuilder.DeleteData(
                table: "icd10_codes",
                keyColumn: "code",
                keyValue: "J06.9");

            migrationBuilder.DeleteData(
                table: "icd10_codes",
                keyColumn: "code",
                keyValue: "J45.909");

            migrationBuilder.DeleteData(
                table: "icd10_codes",
                keyColumn: "code",
                keyValue: "Z00.0");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "icd10_codes",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "is_leaf",
                table: "icd10_codes",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "level",
                table: "icd10_codes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_icd10_codes_description",
                table: "icd10_codes",
                column: "description");

            migrationBuilder.CreateIndex(
                name: "ix_icd10_codes_is_leaf",
                table: "icd10_codes",
                column: "is_leaf");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_icd10_codes_description",
                table: "icd10_codes");

            migrationBuilder.DropIndex(
                name: "ix_icd10_codes_is_leaf",
                table: "icd10_codes");

            migrationBuilder.DropColumn(
                name: "is_leaf",
                table: "icd10_codes");

            migrationBuilder.DropColumn(
                name: "level",
                table: "icd10_codes");

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "icd10_codes",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.InsertData(
                table: "icd10_codes",
                columns: new[] { "code", "description" },
                values: new object[,]
                {
                    { "E11.9", "Type 2 diabetes mellitus without complications" },
                    { "I10", "Essential (primary) hypertension" },
                    { "J06.9", "Acute upper respiratory infection, unspecified" },
                    { "J45.909", "Unspecified asthma, uncomplicated" },
                    { "Z00.0", "General health examination" }
                });
        }
    }
}
