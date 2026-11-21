-- JJQR - AI (21 Nov 2026) OR numbers: hosted-DB migration for EF migration 20261121092808_OrNumberUniqueCloud
-- Makes payments.or_number nvarchar(100) and adds a filtered UNIQUE index on online (cloud-written) payments only
-- (origin_node_id IS NULL). Payments accepted from clinic nodes are outside the index: they keep the OR as printed even when it clashes (flagged, never dropped).
-- Case-insensitive uniqueness follows the column collation (SQL_Latin1_General_CP1_CI_AS = case-insensitive); the preview below checks with the actual collation.
-- Plain T-SQL (no GO separators), idempotent: safe to run twice. Additive/narrowing only; NO row is changed or deleted.
-- Needs the CloudSync migration first (origin_node_id column): run migrate-cloud-sync.sql before this script.
--
-- HOW TO USE (back up first; this has NOT been run against any hosted database):
--   1. Run it as-is. @apply = 0 -> PREVIEW only: shows what is blocking (duplicates / values over 100 chars) and changes nothing.
--   2. Fix every row the preview lists BY HAND (never auto-renumbered here), then set @apply = 1 and run again (one transaction, XACT_ABORT).
--   3. Run again with @apply = 0: the preview lists are empty and the migration row is present.
-- Read-only companion: check-or-duplicates.sql.
SET XACT_ABORT ON;
SET NOCOUNT ON;
DECLARE @apply bit = 0;   -- <<< 0 = preview only, 1 = make the changes

-- ── PREVIEW ────────────────────────────────────────────────────────────────────────────────
SELECT CASE WHEN COL_LENGTH(N'payments', N'origin_node_id') IS NULL
            THEN 'MISSING: run migrate-cloud-sync.sql first (payments.origin_node_id not found)' ELSE 'origin_node_id present' END AS prerequisite;

SELECT COUNT(*) AS or_numbers_over_100_chars_blocking
FROM payments WHERE or_number IS NOT NULL AND LEN(or_number) > 100;

IF COL_LENGTH(N'payments', N'origin_node_id') IS NOT NULL
    EXEC (N'SELECT p.or_number, COUNT(*) AS online_payments_using_it
            FROM payments p
            WHERE p.or_number IS NOT NULL AND p.origin_node_id IS NULL
            GROUP BY p.or_number HAVING COUNT(*) > 1');   -- duplicates the unique index would reject (blocking)

SELECT CAST(COLUMNPROPERTY(OBJECT_ID(N'payments'), N'or_number', 'charmaxlen') AS int) AS or_number_current_max_chars, -- -1 = nvarchar(max)
       (SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'payments') AND name = N'or_number') AS or_number_collation,
       CASE WHEN EXISTS (SELECT * FROM sys.indexes WHERE name = N'ix_payments_or_number_cloud' AND object_id = OBJECT_ID(N'payments'))
            THEN 'index already exists' ELSE 'index not yet created' END AS index_state,
       CASE WHEN EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [migration_id] = N'20261121092808_OrNumberUniqueCloud')
            THEN 'migration already recorded' ELSE 'migration NOT yet recorded' END AS migration_state;

IF @apply = 0
BEGIN
    SELECT 'Preview only: nothing was changed. Fix any blocking rows, set @apply = 1 and run again.' AS result;
    RETURN;
END;

-- Refuse to apply while blocking rows exist (nothing is changed in that case).
IF EXISTS (SELECT * FROM payments WHERE or_number IS NOT NULL AND LEN(or_number) > 100)
BEGIN
    SELECT 'STOPPED: some or_number values are longer than 100 characters. Nothing changed.' AS result;
    RETURN;
END;
IF COL_LENGTH(N'payments', N'origin_node_id') IS NULL
BEGIN
    SELECT 'STOPPED: payments.origin_node_id is missing. Run migrate-cloud-sync.sql first. Nothing changed.' AS result;
    RETURN;
END;
DECLARE @dups int;
EXEC sp_executesql N'SELECT @n = COUNT(*) FROM (SELECT or_number FROM payments WHERE or_number IS NOT NULL AND origin_node_id IS NULL GROUP BY or_number HAVING COUNT(*) > 1) d', N'@n int OUTPUT', @n = @dups OUTPUT;
IF @dups > 0
BEGIN
    SELECT 'STOPPED: duplicate OR numbers exist among online payments (see preview). Fix them by hand first. Nothing changed.' AS result;
    RETURN;
END;

-- ── APPLY (same operations as the EF migration) ────────────────────────────────────────────
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121092808_OrNumberUniqueCloud'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[payments]') AND [c].[name] = N'or_number');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [payments] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [payments] ALTER COLUMN [or_number] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121092808_OrNumberUniqueCloud'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [ix_payments_or_number_cloud] ON [payments] ([or_number]) WHERE [or_number] IS NOT NULL AND [origin_node_id] IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121092808_OrNumberUniqueCloud'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20261121092808_OrNumberUniqueCloud', N'10.0.11');
END;

COMMIT;

SELECT 'Done. Run it again with @apply = 0 to confirm.' AS result;
-- JJQR - END
