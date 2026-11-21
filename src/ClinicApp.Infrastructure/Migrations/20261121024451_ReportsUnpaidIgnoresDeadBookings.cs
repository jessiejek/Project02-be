using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReportsUnpaidIgnoresDeadBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //JJQR - AI (Nov 21, 2026) - a cancelled / no-show / expired booking owes nothing, so the daily report no longer counts its leftover "Unpaid" payment as unpaid.
            // CREATE VIEW is wrapped in EXEC so it also works inside the IF block of an idempotent script (SQL Server needs CREATE VIEW alone in its batch).
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_daily_booking_summary;");
            migrationBuilder.Sql(@"EXEC(N'CREATE VIEW v_daily_booking_summary AS
SELECT
  b.appointment_date,
  COUNT(*) AS total_bookings,
  SUM(CASE WHEN b.status = ''Completed'' THEN 1 ELSE 0 END) AS completed_count,
  SUM(CASE WHEN pay.status = ''Paid'' THEN 1 ELSE 0 END) AS paid_count,
  SUM(CASE WHEN pay.status = ''Unpaid'' AND b.status NOT IN (''Cancelled'', ''NoShow'', ''Expired'', ''Rescheduled'') THEN 1 ELSE 0 END) AS unpaid_count,
  SUM(CASE WHEN b.status = ''NoShow'' THEN 1 ELSE 0 END) AS no_show_count,
  COALESCE(SUM(CASE WHEN pay.status = ''Paid'' THEN COALESCE(pay.amount_received, pay.amount) ELSE 0 END), 0) AS revenue
FROM bookings b
LEFT JOIN payments pay ON pay.booking_id = b.booking_id
GROUP BY b.appointment_date;')");
            //JJQR - END
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_daily_booking_summary;");
            migrationBuilder.Sql(@"EXEC(N'CREATE VIEW v_daily_booking_summary AS
SELECT
  b.appointment_date,
  COUNT(*) AS total_bookings,
  SUM(CASE WHEN b.status = ''Completed'' THEN 1 ELSE 0 END) AS completed_count,
  SUM(CASE WHEN pay.status = ''Paid'' THEN 1 ELSE 0 END) AS paid_count,
  SUM(CASE WHEN pay.status = ''Unpaid'' THEN 1 ELSE 0 END) AS unpaid_count,
  SUM(CASE WHEN b.status = ''NoShow'' THEN 1 ELSE 0 END) AS no_show_count,
  COALESCE(SUM(CASE WHEN pay.status = ''Paid'' THEN COALESCE(pay.amount_received, pay.amount) ELSE 0 END), 0) AS revenue
FROM bookings b
LEFT JOIN payments pay ON pay.booking_id = b.booking_id
GROUP BY b.appointment_date;')");
        }
    }
}
