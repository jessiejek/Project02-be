//JJQR - AI (21 Nov 2026) OR numbers: or_number nvarchar(100) + filtered unique index on ONLINE (origin_node_id IS NULL) payments; case-insensitive per column collation
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrNumberUniqueCloud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "or_number",
                table: "payments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_or_number_cloud",
                table: "payments",
                column: "or_number",
                unique: true,
                filter: "[or_number] IS NOT NULL AND [origin_node_id] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_or_number_cloud",
                table: "payments");

            migrationBuilder.AlterColumn<string>(
                name: "or_number",
                table: "payments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
//JJQR - END
