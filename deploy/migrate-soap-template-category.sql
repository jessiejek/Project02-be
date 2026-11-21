-- Adds soap_templates.category ("Adult" | "Pedia" | NULL) - migration 20261121132750_SoapTemplateCategory. Safe to run more than once.
IF COL_LENGTH('soap_templates', 'category') IS NULL
    ALTER TABLE soap_templates ADD category nvarchar(max) NULL;
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE migration_id = N'20261121132750_SoapTemplateCategory')
    INSERT INTO __EFMigrationsHistory (migration_id, product_version) VALUES (N'20261121132750_SoapTemplateCategory', N'10.0.11');
