-- JJQR - AI (Nov 19, 2026) - Templates revamp (Phase 2) for the hosted DB. Plain T-SQL (no GO separators).
-- Idempotent: safe to run twice (skipped when the EF migration is already recorded; each ALTER also checks the column).
-- Mirrors EF migration 20261119103723_TemplatesRevamp. Additive only: nullable columns / NOT NULL DEFAULT 0, no data change.
-- NOT applied to any server by the author — review, back up, then run it yourself.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261119103723_TemplatesRevamp'
)
BEGIN
    IF COL_LENGTH(N'soap_phrases', N'sort_order') IS NULL
        ALTER TABLE [soap_phrases] ADD [sort_order] int NOT NULL DEFAULT 0;
    IF COL_LENGTH(N'doctor_favorite_medicines', N'sort_order') IS NULL
        ALTER TABLE [doctor_favorite_medicines] ADD [sort_order] int NOT NULL DEFAULT 0;
    IF COL_LENGTH(N'doctor_diagnosis_templates', N'icd10code') IS NULL
        ALTER TABLE [doctor_diagnosis_templates] ADD [icd10code] nvarchar(max) NULL;
    IF COL_LENGTH(N'prescription_template_items', N'timing') IS NULL
        ALTER TABLE [prescription_template_items] ADD [timing] nvarchar(max) NULL;
    IF COL_LENGTH(N'prescription_template_items', N'meal_relation') IS NULL
        ALTER TABLE [prescription_template_items] ADD [meal_relation] nvarchar(max) NULL;
    IF COL_LENGTH(N'prescription_template_items', N'duration_kind') IS NULL
        ALTER TABLE [prescription_template_items] ADD [duration_kind] nvarchar(max) NULL;
    IF COL_LENGTH(N'prescription_template_items', N'duration_value') IS NULL
        ALTER TABLE [prescription_template_items] ADD [duration_value] int NULL;
    IF COL_LENGTH(N'prescription_template_items', N'indication') IS NULL
        ALTER TABLE [prescription_template_items] ADD [indication] nvarchar(max) NULL;
    IF COL_LENGTH(N'prescription_template_items', N'route') IS NULL
        ALTER TABLE [prescription_template_items] ADD [route] nvarchar(max) NULL;
END;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20261119103723_TemplatesRevamp'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20261119103723_TemplatesRevamp', N'10.0.11');
END;
COMMIT;
