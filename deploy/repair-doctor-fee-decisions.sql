-- Repair visits whose PAYMENT does not match the doctor's fee decision. SQL Server (local Docker DB or hosted). Plain T-SQL, no GO, safe to run twice.
--
-- Why: until the fix of Nov 21, 2026 the doctor's decision was only saved as a note on the consultation:
--   1) a visit the doctor WAIVED stayed "Unpaid" and showed up on "Collect payments";
--   2) a doctor's CHARGE amount (e.g. 300) was ignored: staff were asked to collect the standard clinic fee (e.g. 450),
--      and the payment's own amount (the one checked when staff record a payment) was never updated after check-in.
--
-- HOW TO USE: run it once as-is. It only SHOWS what would change (the preview tables). Read them.
-- (Only FINISHED consultations are followed, the same as the app itself; a draft never changes an amount.)
-- Then change  @apply  to 1  and run it again to make the changes. Nothing that is already Paid / Waived / Refunded is ever touched.

DECLARE @apply bit = 0;   -- <<< 0 = preview only, 1 = make the changes
DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();

-- ── PREVIEW ────────────────────────────────────────────────────────────────────────────────
SELECT 'WAIVE: doctor waived, payment still Unpaid' AS fix,
       b.booking_id, b.queue_number, b.appointment_date, p.status AS payment_status, p.amount AS payment_amount,
       b.amount_due, c.pf_waive_reason
FROM payments p
JOIN consultations c ON c.booking_id = p.booking_id
JOIN bookings b ON b.booking_id = p.booking_id
WHERE p.status = 'Unpaid'
  AND c.pf_decision = 'Waive'
  AND LEN(LTRIM(RTRIM(ISNULL(c.pf_waive_reason, '')))) > 0
  AND c.status IN ('Completed', 'Amended');

SELECT 'CHARGE: doctor charged a different amount' AS fix,
       b.booking_id, b.queue_number, b.appointment_date, p.amount AS payment_amount, b.amount_due, b.total_fee,
       c.pf_amount AS doctor_amount
FROM payments p
JOIN consultations c ON c.booking_id = p.booking_id
JOIN bookings b ON b.booking_id = p.booking_id
WHERE p.status = 'Unpaid'
  AND c.pf_decision = 'Charge' AND c.status IN ('Completed', 'Amended')
  AND c.pf_amount > 0 AND c.pf_amount <= 100000
  AND (p.amount <> c.pf_amount OR b.amount_due <> c.pf_amount OR b.total_fee <> c.pf_amount);

SELECT 'STALE: payment amount differs from the amount shown as due' AS fix,
       b.booking_id, b.queue_number, b.appointment_date, p.amount AS payment_amount, b.amount_due
FROM payments p
JOIN bookings b ON b.booking_id = p.booking_id
WHERE p.status = 'Unpaid' AND b.amount_due > 0 AND p.amount <> b.amount_due;

-- ── APPLY (only when @apply = 1) ───────────────────────────────────────────────────────────
IF @apply = 1
BEGIN
    BEGIN TRANSACTION;

    -- 1) a visit the doctor waived is waived: nothing left to collect
    UPDATE p
       SET p.status = 'Waived',
           p.waived_reason = c.pf_waive_reason,
           p.waived_by_user_id = c.completed_by_user_id,
           p.waived_at = @now,
           p.updated_at = @now
    FROM payments p
    JOIN consultations c ON c.booking_id = p.booking_id
    WHERE p.status = 'Unpaid'
      AND c.pf_decision = 'Waive'
      AND LEN(LTRIM(RTRIM(ISNULL(c.pf_waive_reason, '')))) > 0
      AND c.status IN ('Completed', 'Amended');

    UPDATE b
       SET b.amount_due = 0, b.updated_at = @now
    FROM bookings b
    JOIN payments p ON p.booking_id = b.booking_id
    WHERE p.status = 'Waived' AND b.amount_due <> 0;

    -- 2) the doctor's charge amount is what is collected (payment amount, amount due and total)
    UPDATE p
       SET p.amount = c.pf_amount, p.updated_at = @now
    FROM payments p
    JOIN consultations c ON c.booking_id = p.booking_id
    WHERE p.status = 'Unpaid'
      AND c.pf_decision = 'Charge' AND c.status IN ('Completed', 'Amended')
      AND c.pf_amount > 0 AND c.pf_amount <= 100000
      AND p.amount <> c.pf_amount;

    UPDATE b
       SET b.amount_due = c.pf_amount, b.total_fee = c.pf_amount, b.updated_at = @now
    FROM bookings b
    JOIN consultations c ON c.booking_id = b.booking_id
    JOIN payments p ON p.booking_id = b.booking_id
    WHERE p.status = 'Unpaid'
      AND c.pf_decision = 'Charge' AND c.status IN ('Completed', 'Amended')
      AND c.pf_amount > 0 AND c.pf_amount <= 100000
      AND (b.amount_due <> c.pf_amount OR b.total_fee <> c.pf_amount);

    -- 3) anything still Unpaid whose payment amount is out of step with the amount due (an old check-in amount): follow the amount due
    UPDATE p
       SET p.amount = b.amount_due, p.updated_at = @now
    FROM payments p
    JOIN bookings b ON b.booking_id = p.booking_id
    WHERE p.status = 'Unpaid' AND b.amount_due > 0 AND p.amount <> b.amount_due;

    COMMIT TRANSACTION;
    SELECT 'Done. Run it again with @apply = 0 to confirm the preview tables are empty.' AS result;
END
ELSE
    SELECT 'Preview only: nothing was changed. Set @apply = 1 and run again to apply.' AS result;
