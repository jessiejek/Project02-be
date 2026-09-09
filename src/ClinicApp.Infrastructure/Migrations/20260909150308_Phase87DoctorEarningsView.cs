using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase87DoctorEarningsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // §16.9 — monthly earnings per doctor over completed visits. Payment is
            // 1:1 with a booking so the LEFT JOIN never fans out the totals.
            migrationBuilder.Sql(@"
CREATE VIEW v_doctor_earnings AS
SELECT
  b.doctor_id,
  CONVERT(char(7), b.appointment_date, 126) AS period,
  COUNT(DISTINCT b.booking_id) AS completed_visits,
  COALESCE(SUM(b.total_fee), 0) AS gross_billed,
  COALESCE(SUM(CASE WHEN pay.status = 'Paid' THEN pay.amount ELSE 0 END), 0) AS collected,
  COALESCE(SUM(CASE WHEN pay.status = 'Waived' THEN pay.amount ELSE 0 END), 0) AS waived
FROM bookings b
LEFT JOIN payments pay ON pay.booking_id = b.booking_id
WHERE b.status = 'Completed'
GROUP BY b.doctor_id, CONVERT(char(7), b.appointment_date, 126);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_doctor_earnings;");
        }
    }
}
