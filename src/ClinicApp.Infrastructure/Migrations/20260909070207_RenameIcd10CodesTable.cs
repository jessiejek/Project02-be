using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameIcd10CodesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_icd10codes",
                table: "icd10codes");

            migrationBuilder.RenameTable(
                name: "icd10codes",
                newName: "icd10_codes");

            migrationBuilder.AddPrimaryKey(
                name: "pk_icd10_codes",
                table: "icd10_codes",
                column: "code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_icd10_codes",
                table: "icd10_codes");

            migrationBuilder.RenameTable(
                name: "icd10_codes",
                newName: "icd10codes");

            migrationBuilder.AddPrimaryKey(
                name: "pk_icd10codes",
                table: "icd10codes",
                column: "code");
        }
    }
}
