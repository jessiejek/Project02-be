using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase95FixEarningsViewCollectedAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Root cause (live bug, traced from a real production record — Maria
            // Santos / MF-100000, booking 915b5952-…): `payments.amount` is a
            // snapshot taken when the payment row is first created at check-in
            // time (walk-in queue / booking creation), computed from the base
            // consultation fee only. When the doctor later requests a medical
            // certificate or applies a discount, ConsultationsController.
            // UpsertByBooking recomputes `bookings.total_fee`/`amount_due` — but
            // nothing ever re-syncs `payments.amount` to match. For that booking:
            // total_fee = 500 (450 base + 50 med cert), but payments.amount was
            // still 450 from check-in. Staff correctly entered/received the full
            // ₱500 (payments.amount_received = 500, booking.amount_due correctly
            // zeroed) — the money was never actually short. But three reporting
            // views summed the stale `payments.amount` column instead of the
            // fields that are actually kept fresh, so the doctor's earnings
            // dashboard displayed "Collected: ₱450" for a fully-paid ₱500 visit.
            //
            // Fix: stop reading `payments.amount` for "what's owed/collected" in
            // every view that did — use the columns that are actually correct:
            //   - collected (Paid)  -> payments.amount_received (what staff
            //     actually recorded receiving), falling back to payments.amount
            //     only for legacy rows where amount_received was never set.
            //   - waived (Waived)   -> bookings.total_fee (there's no separate
            //     "amount waived" input — Confirm/Waive both unconditionally
            //     zero amount_due, so the forgiven amount is the fee that was
            //     due at that point, which total_fee already tracks correctly).
            //   - amount_due (unpaid completed visits) -> bookings.amount_due,
            //     which ConsultationsController keeps in sync on every fee
            //     recompute, unlike payments.amount.
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_doctor_earnings;");
            migrationBuilder.Sql(@"
CREATE VIEW v_doctor_earnings AS
SELECT
  b.doctor_id,
  CONVERT(char(7), b.appointment_date, 126) AS period,
  COUNT(DISTINCT b.booking_id) AS completed_visits,
  COALESCE(SUM(b.total_fee), 0) AS gross_billed,
  COALESCE(SUM(CASE WHEN pay.status = 'Paid' THEN COALESCE(pay.amount_received, pay.amount) ELSE 0 END), 0) AS collected,
  COALESCE(SUM(CASE WHEN pay.status = 'Waived' THEN b.total_fee ELSE 0 END), 0) AS waived
FROM bookings b
LEFT JOIN payments pay ON pay.booking_id = b.booking_id
WHERE b.status = 'Completed'
GROUP BY b.doctor_id, CONVERT(char(7), b.appointment_date, 126);");

            migrationBuilder.Sql("DROP VIEW IF EXISTS v_daily_booking_summary;");
            migrationBuilder.Sql(@"
CREATE VIEW v_daily_booking_summary AS
SELECT
  b.appointment_date,
  COUNT(*) AS total_bookings,
  SUM(CASE WHEN b.status = 'Completed' THEN 1 ELSE 0 END) AS completed_count,
  SUM(CASE WHEN pay.status = 'Paid' THEN 1 ELSE 0 END) AS paid_count,
  SUM(CASE WHEN pay.status = 'Unpaid' THEN 1 ELSE 0 END) AS unpaid_count,
  SUM(CASE WHEN b.status = 'NoShow' THEN 1 ELSE 0 END) AS no_show_count,
  COALESCE(SUM(CASE WHEN pay.status = 'Paid' THEN COALESCE(pay.amount_received, pay.amount) ELSE 0 END), 0) AS revenue
FROM bookings b
LEFT JOIN payments pay ON pay.booking_id = b.booking_id
GROUP BY b.appointment_date;");

            migrationBuilder.Sql("DROP VIEW IF EXISTS v_unpaid_completed_visits;");
            migrationBuilder.Sql(@"
CREATE VIEW v_unpaid_completed_visits AS
SELECT
  b.booking_id,
  b.patient_id,
  p.patient_code,
  p.first_name + ' ' + p.last_name AS patient_name,
  b.doctor_id,
  sa.full_name AS doctor_name,
  b.appointment_date,
  b.amount_due AS amount_due,
  pay.status AS payment_status
FROM bookings b
JOIN patients p ON p.patient_id = b.patient_id
JOIN doctors d ON d.doctor_id = b.doctor_id
JOIN staff_accounts sa ON sa.staff_id = d.doctor_id
JOIN payments pay ON pay.booking_id = b.booking_id
WHERE b.status = 'Completed' AND pay.status = 'Unpaid';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_doctor_earnings;");
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

            migrationBuilder.Sql("DROP VIEW IF EXISTS v_daily_booking_summary;");
            migrationBuilder.Sql(@"
CREATE VIEW v_daily_booking_summary AS
SELECT
  b.appointment_date,
  COUNT(*) AS total_bookings,
  SUM(CASE WHEN b.status = 'Completed' THEN 1 ELSE 0 END) AS completed_count,
  SUM(CASE WHEN pay.status = 'Paid' THEN 1 ELSE 0 END) AS paid_count,
  SUM(CASE WHEN pay.status = 'Unpaid' THEN 1 ELSE 0 END) AS unpaid_count,
  SUM(CASE WHEN b.status = 'NoShow' THEN 1 ELSE 0 END) AS no_show_count,
  COALESCE(SUM(CASE WHEN pay.status = 'Paid' THEN pay.amount ELSE 0 END), 0) AS revenue
FROM bookings b
LEFT JOIN payments pay ON pay.booking_id = b.booking_id
GROUP BY b.appointment_date;");

            migrationBuilder.Sql("DROP VIEW IF EXISTS v_unpaid_completed_visits;");
            migrationBuilder.Sql(@"
CREATE VIEW v_unpaid_completed_visits AS
SELECT
  b.booking_id,
  b.patient_id,
  p.patient_code,
  p.first_name + ' ' + p.last_name AS patient_name,
  b.doctor_id,
  sa.full_name AS doctor_name,
  b.appointment_date,
  pay.amount AS amount_due,
  pay.status AS payment_status
FROM bookings b
JOIN patients p ON p.patient_id = b.patient_id
JOIN doctors d ON d.doctor_id = b.doctor_id
JOIN staff_accounts sa ON sa.staff_id = d.doctor_id
JOIN payments pay ON pay.booking_id = b.booking_id
WHERE b.status = 'Completed' AND pay.status = 'Unpaid';");
        }
    }
}
