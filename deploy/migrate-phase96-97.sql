BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260913115727_Phase96BookingCreatedBy'
)
BEGIN
    ALTER TABLE [bookings] ADD [created_by_user_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260913115727_Phase96BookingCreatedBy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260913115727_Phase96BookingCreatedBy', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260918044940_Phase97QueueCounter'
)
BEGIN
    CREATE TABLE [queue_counters] (
        [date] date NOT NULL,
        [value] int NOT NULL,
        CONSTRAINT [pk_queue_counters] PRIMARY KEY ([date])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260918044940_Phase97QueueCounter'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260918044940_Phase97QueueCounter', N'10.0.11');
END;

COMMIT;
GO

