-- Queue lanes (R = regular, S = senior/PWD) for the hosted DB. Plain T-SQL (no GO separators), idempotent: safe to run twice.
-- Run this BEFORE deploying the API build that uses lanes. Independent of the other pending migrations (only touches queue_counters).
-- Existing counter rows become lane 'R', so today's running number simply continues as R-xxx.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121015340_QueueLanes'
)
BEGIN
    ALTER TABLE [queue_counters] DROP CONSTRAINT [pk_queue_counters];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121015340_QueueLanes'
)
BEGIN
    ALTER TABLE [queue_counters] ADD [lane] nvarchar(1) NOT NULL DEFAULT N'R';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121015340_QueueLanes'
)
BEGIN
    ALTER TABLE [queue_counters] ADD CONSTRAINT [pk_queue_counters] PRIMARY KEY ([date], [lane]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261121015340_QueueLanes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20261121015340_QueueLanes', N'10.0.11');
END;

COMMIT;
