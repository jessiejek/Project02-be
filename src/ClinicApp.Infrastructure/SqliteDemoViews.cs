using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

/// <summary>SQLite-syntax equivalents of the 4 reporting views created via raw T-SQL in the
/// SQL Server migration (Migrations/20260907114641_InitialCreate.cs) — ported from
/// supabase/schema.sql lines 830-886, using SQLite's `||` concat and `SUM(CASE WHEN...)` instead
/// of SQL Server's `+`. Demo/local (SQLite) path only; the SQL Server migration is unaffected and
/// remains the deployment source of truth.</summary>
public static class SqliteDemoViews
{
    public static void CreateViews(ClinicAppDbContext db)
    {
        db.Database.ExecuteSqlRaw("DROP VIEW IF EXISTS v_unpaid_completed_visits;");
        db.Database.ExecuteSqlRaw(@"
CREATE VIEW v_unpaid_completed_visits AS
SELECT
  b.booking_id,
  b.patient_id,
  p.patient_code,
  p.first_name || ' ' || p.last_name AS patient_name,
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

        db.Database.ExecuteSqlRaw("DROP VIEW IF EXISTS v_pending_follow_ups;");
        db.Database.ExecuteSqlRaw(@"
CREATE VIEW v_pending_follow_ups AS
SELECT
  f.id AS follow_up_id,
  f.patient_id,
  p.first_name || ' ' || p.last_name AS patient_name,
  f.doctor_id,
  sa.full_name AS doctor_name,
  f.follow_up_date,
  f.reason,
  f.status
FROM follow_ups f
JOIN patients p ON p.patient_id = f.patient_id
JOIN doctors d ON d.doctor_id = f.doctor_id
JOIN staff_accounts sa ON sa.staff_id = d.doctor_id
WHERE f.status = 'Pending';");

        db.Database.ExecuteSqlRaw("DROP VIEW IF EXISTS v_daily_booking_summary;");
        db.Database.ExecuteSqlRaw(@"
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

        db.Database.ExecuteSqlRaw("DROP VIEW IF EXISTS v_doctor_ratings;");
        db.Database.ExecuteSqlRaw(@"
CREATE VIEW v_doctor_ratings AS
SELECT
  d.doctor_id,
  COALESCE(ROUND(AVG(r.rating), 2), 0) AS average_rating,
  COUNT(r.review_id) AS review_count
FROM doctors d
LEFT JOIN reviews r ON r.doctor_id = d.doctor_id
GROUP BY d.doctor_id;");
    }
}
