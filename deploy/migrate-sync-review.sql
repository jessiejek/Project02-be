-- Phase 4 (offline-mode plan): sync review items + patient merge pointer.
-- Migrations 20261122080550_AddSyncReviewItems and 20261122081514_AddPatientMergedInto. Safe to run more than once. Run it in the host's SQL panel (or sqlcmd) BEFORE the new backend goes live.
SET QUOTED_IDENTIFIER ON; -- needed for the filtered index below
SET ANSI_NULLS ON;
IF OBJECT_ID(N'sync_review_items', N'U') IS NULL
BEGIN
    CREATE TABLE [sync_review_items] (
        [id] uniqueidentifier NOT NULL,
        [kind] nvarchar(40) NOT NULL,
        [status] nvarchar(16) NOT NULL,
        [title] nvarchar(300) NOT NULL,
        [details_json] nvarchar(max) NULL,
        [patient_id] uniqueidentifier NULL,
        [other_patient_id] uniqueidentifier NULL,
        [aggregate] nvarchar(64) NULL,
        [entity_id] nvarchar(100) NULL,
        [other_entity_id] nvarchar(100) NULL,
        [origin_node_id] uniqueidentifier NULL,
        [created_at] datetimeoffset NOT NULL,
        [resolved_at] datetimeoffset NULL,
        [resolved_by_user_id] uniqueidentifier NULL,
        [resolution] nvarchar(16) NULL,
        [resolution_note] nvarchar(1000) NULL,
        CONSTRAINT [pk_sync_review_items] PRIMARY KEY ([id])
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_sync_review_items_kind_entity_id_other_entity_id' AND object_id = OBJECT_ID(N'sync_review_items'))
    CREATE UNIQUE INDEX [ix_sync_review_items_kind_entity_id_other_entity_id] ON [sync_review_items] ([kind], [entity_id], [other_entity_id]) WHERE [entity_id] IS NOT NULL AND [other_entity_id] IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_sync_review_items_status_created_at' AND object_id = OBJECT_ID(N'sync_review_items'))
    CREATE INDEX [ix_sync_review_items_status_created_at] ON [sync_review_items] ([status], [created_at]);
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE migration_id = N'20261122080550_AddSyncReviewItems')
    INSERT INTO __EFMigrationsHistory (migration_id, product_version) VALUES (N'20261122080550_AddSyncReviewItems', N'10.0.11');

IF COL_LENGTH('patients', 'merged_into_patient_id') IS NULL
    ALTER TABLE [patients] ADD [merged_into_patient_id] uniqueidentifier NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_patients_merged_into_patient_id' AND object_id = OBJECT_ID(N'patients'))
    EXEC(N'CREATE INDEX [ix_patients_merged_into_patient_id] ON [patients] ([merged_into_patient_id])'); -- EXEC: the column is new in this same batch
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE migration_id = N'20261122081514_AddPatientMergedInto')
    INSERT INTO __EFMigrationsHistory (migration_id, product_version) VALUES (N'20261122081514_AddPatientMergedInto', N'10.0.11');
