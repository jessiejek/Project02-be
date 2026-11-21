-- (1) Daily booking report: cancelled / no-show / expired / rescheduled bookings are no longer counted as "unpaid" (replaces the view v_daily_booking_summary).
-- (2) Waivers recorded before the "Confirm waiver" step existed are marked confirmed, so the Payments page only shows NEW doctor-waived visits.
-- Plain T-SQL (no GO separators), idempotent: safe to run twice. Run it BEFORE deploying the API build that includes it. No table changes.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121024451_ReportsUnpaidIgnoresDeadBookings'
)
BEGIN
    DROP VIEW IF EXISTS v_daily_booking_summary;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121024451_ReportsUnpaidIgnoresDeadBookings'
)
BEGIN
    EXEC(N'CREATE VIEW v_daily_booking_summary AS
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
    GROUP BY b.appointment_date;')
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121024451_ReportsUnpaidIgnoresDeadBookings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20261121024451_ReportsUnpaidIgnoresDeadBookings', N'10.0.11');
END;

COMMIT;

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121033144_BackfillOldWaiversConfirmed'
)
BEGIN

    UPDATE payments
    SET confirmed_by_user_id = waived_by_user_id,
        confirmed_at = COALESCE(waived_at, SYSDATETIMEOFFSET())
    WHERE status = 'Waived'
      AND confirmed_at IS NULL
      AND (waived_at IS NULL OR waived_at < '2026-11-21T00:00:00+00:00');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121033144_BackfillOldWaiversConfirmed'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20261121033144_BackfillOldWaiversConfirmed', N'10.0.11');
END;

COMMIT;
