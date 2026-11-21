using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QueueLanes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_queue_counters",
                table: "queue_counters");

            migrationBuilder.AddColumn<string>(
                name: "lane",
                table: "queue_counters",
                type: "nvarchar(1)",
                maxLength: 1,
                nullable: false,
                //JJQR - AI (Nov 21, 2026) - existing counter rows (pre-lane "Q-" numbers) carry over as the regular lane
                defaultValue: "R");
                //JJQR - END

            migrationBuilder.AddPrimaryKey(
                name: "pk_queue_counters",
                table: "queue_counters",
                columns: new[] { "date", "lane" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_queue_counters",
                table: "queue_counters");

            migrationBuilder.DropColumn(
                name: "lane",
                table: "queue_counters");

            migrationBuilder.AddPrimaryKey(
                name: "pk_queue_counters",
                table: "queue_counters",
                column: "date");
        }
    }
}
