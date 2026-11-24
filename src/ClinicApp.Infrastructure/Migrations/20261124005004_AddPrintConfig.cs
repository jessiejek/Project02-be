using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "print_configs",
                columns: table => new
                {
                    key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    text_value = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    content_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    data = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_print_configs", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "print_configs");
        }
    }
}
