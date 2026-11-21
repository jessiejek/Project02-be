using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillOldWaiversConfirmed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //JJQR - AI (Nov 21, 2026) - waivers recorded BEFORE the "confirm waiver" step existed were already handled at the desk: mark them confirmed so the Payments page does not show them as new work.
            // Only waivers before 2026-11-21 (UTC); doctor-waived visits from the new flow stay unconfirmed until the front desk confirms them.
            migrationBuilder.Sql(@"
UPDATE payments
SET confirmed_by_user_id = waived_by_user_id,
    confirmed_at = COALESCE(waived_at, SYSDATETIMEOFFSET())
WHERE status = 'Waived'
  AND confirmed_at IS NULL
  AND (waived_at IS NULL OR waived_at < '2026-11-21T00:00:00+00:00');");
            //JJQR - END
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
