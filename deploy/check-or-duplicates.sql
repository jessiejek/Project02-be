-- JJQR - AI (21 Nov 2026) READ-ONLY check: duplicate / unusable OR numbers in payments (run BEFORE any unique-index migration is even considered)
-- This script only SELECTs. It changes nothing, has no @apply switch, and is NOT part of any automatic deploy: run it by hand against a copy or the live DB.
-- Plain T-SQL (no GO separators). Nothing here has been run yet.
--
-- HOW TO READ THE RESULT
--   1. duplicate_or_numbers      : every OR number used by more than one payment (exact match, then ignoring case / blanks).
--   2. duplicate_or_payments     : the payments behind those duplicates (which node wrote them, status, when).
--   3. or_numbers_too_long       : OR numbers over 100 characters (a unique index needs a narrower column than nvarchar(max)).
--   4. blank_or_numbers          : empty / whitespace-only OR numbers (should be NULL instead).
--   5. or_number_summary         : totals so you can see the size of the problem.
-- If sets 1-4 are empty, the data is clean enough for a unique index. If not, the rows are listed for manual review; nothing is renumbered.
SET NOCOUNT ON;

-- 1. exact duplicates (NULLs ignored)
SELECT p.or_number, COUNT(*) AS payments_using_it
FROM payments p
WHERE p.or_number IS NOT NULL AND LTRIM(RTRIM(p.or_number)) <> N''
GROUP BY p.or_number
HAVING COUNT(*) > 1
ORDER BY COUNT(*) DESC, p.or_number;

-- 1b. duplicates that only differ by case / surrounding spaces (a normalised unique index would reject these too)
SELECT UPPER(LTRIM(RTRIM(p.or_number))) AS normalised_or_number, COUNT(*) AS payments_using_it, COUNT(DISTINCT p.or_number) AS spellings
FROM payments p
WHERE p.or_number IS NOT NULL AND LTRIM(RTRIM(p.or_number)) <> N''
GROUP BY UPPER(LTRIM(RTRIM(p.or_number)))
HAVING COUNT(*) > 1 AND COUNT(DISTINCT p.or_number) > 1
ORDER BY COUNT(*) DESC;

-- 2. the payments behind the exact duplicates (no patient data). origin_node_id only exists once the CloudSync migration is applied, so the query is built dynamically.
DECLARE @origin nvarchar(max) = CASE WHEN COL_LENGTH(N'payments', N'origin_node_id') IS NULL
    THEN N'CAST(NULL AS uniqueidentifier) AS origin_node_id, ''unknown (CloudSync not applied)'' AS written_by'
    ELSE N'p.origin_node_id, CASE WHEN p.origin_node_id IS NULL THEN ''cloud (web/app)'' ELSE ''clinic node'' END AS written_by' END;
DECLARE @sql nvarchar(max) = N'SELECT p.or_number, p.payment_id, p.booking_id, p.status, p.amount, p.confirmed_at, p.created_at, ' + @origin + N'
FROM payments p
WHERE p.or_number IN (
    SELECT q.or_number FROM payments q
    WHERE q.or_number IS NOT NULL AND LTRIM(RTRIM(q.or_number)) <> N''''
    GROUP BY q.or_number HAVING COUNT(*) > 1)
ORDER BY p.or_number, p.created_at;';
EXEC sp_executesql @sql;

-- 3. too long for a narrower indexed column
SELECT p.payment_id, LEN(p.or_number) AS or_length, LEFT(p.or_number, 40) AS or_number_start
FROM payments p
WHERE p.or_number IS NOT NULL AND LEN(p.or_number) > 100;

-- 4. blank (not NULL) values
SELECT p.payment_id, p.status, p.confirmed_at
FROM payments p
WHERE p.or_number IS NOT NULL AND LTRIM(RTRIM(p.or_number)) = N'';

-- 5. summary
SELECT COUNT(*) AS payments_total,
       SUM(CASE WHEN or_number IS NOT NULL AND LTRIM(RTRIM(or_number)) <> N'' THEN 1 ELSE 0 END) AS with_or_number,
       SUM(CASE WHEN or_number LIKE N'OR-????-????????-%' THEN 1 ELSE 0 END) AS desktop_format,
       SUM(CASE WHEN or_number LIKE N'OR-[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]' THEN 1 ELSE 0 END) AS web_booking_format
FROM payments;
-- JJQR - END
