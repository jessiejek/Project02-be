IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [migration_id] nvarchar(150) NOT NULL,
        [product_version] nvarchar(32) NOT NULL,
        CONSTRAINT [pk___ef_migrations_history] PRIMARY KEY ([migration_id])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [announcements] (
        [id] uniqueidentifier NOT NULL,
        [title] nvarchar(max) NOT NULL,
        [body] nvarchar(max) NOT NULL,
        [is_active] bit NOT NULL,
        [posted_by_user_id] uniqueidentifier NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_announcements] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [audit_logs] (
        [id] uniqueidentifier NOT NULL,
        [entity_type] nvarchar(max) NOT NULL,
        [entity_id] uniqueidentifier NOT NULL,
        [action] nvarchar(max) NOT NULL,
        [performed_by_user_id] uniqueidentifier NULL,
        [details] nvarchar(max) NULL,
        [performed_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_audit_logs] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [clinic_accepted_payment_methods] (
        [payment_method] nvarchar(450) NOT NULL,
        CONSTRAINT [pk_clinic_accepted_payment_methods] PRIMARY KEY ([payment_method])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [clinic_operating_hours] (
        [day_of_week] smallint NOT NULL,
        [is_closed] bit NOT NULL,
        [open_time] time NULL,
        [close_time] time NULL,
        CONSTRAINT [pk_clinic_operating_hours] PRIMARY KEY ([day_of_week])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [clinic_settings] (
        [id] smallint NOT NULL IDENTITY,
        [clinic_name] nvarchar(max) NOT NULL,
        [address] nvarchar(max) NOT NULL,
        [contact_number] nvarchar(max) NULL,
        [email] nvarchar(max) NULL,
        [description] nvarchar(max) NULL,
        [default_payment_mode] nvarchar(max) NOT NULL,
        [refund_policy] nvarchar(max) NULL,
        [consent_version] int NOT NULL,
        [primary_color] nvarchar(max) NULL,
        [secondary_color] nvarchar(max) NULL,
        [logo_url] nvarchar(max) NULL,
        [favicon_url] nvarchar(max) NULL,
        [website_url] nvarchar(max) NULL,
        [privacy_policy_text] nvarchar(max) NULL,
        [updated_by_user_id] uniqueidentifier NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_clinic_settings] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [consultation_diagnoses] (
        [id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NOT NULL,
        [icd10code] nvarchar(max) NULL,
        [custom_description] nvarchar(max) NULL,
        [type] nvarchar(max) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_consultation_diagnoses] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [doctor_favorite_medicines] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [medicine_id] uniqueidentifier NOT NULL,
        [generic_name] nvarchar(max) NOT NULL,
        [dosage] nvarchar(max) NOT NULL,
        [quantity] nvarchar(max) NOT NULL,
        [instruction] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctor_favorite_medicines] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [follow_ups] (
        [id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [follow_up_date] date NOT NULL,
        [reason] nvarchar(max) NULL,
        [instructions] nvarchar(max) NULL,
        [reminder_enabled] bit NOT NULL,
        [status] nvarchar(max) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_follow_ups] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [icd10codes] (
        [code] nvarchar(450) NOT NULL,
        [description] nvarchar(max) NOT NULL,
        CONSTRAINT [pk_icd10codes] PRIMARY KEY ([code])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [lab_orders] (
        [lab_order_id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [test_name] nvarchar(max) NOT NULL,
        [test_code] nvarchar(max) NULL,
        [reason] nvarchar(max) NULL,
        [clinical_indication] nvarchar(max) NULL,
        [specimen_type] nvarchar(max) NULL,
        [notes] nvarchar(max) NULL,
        [status] nvarchar(max) NOT NULL,
        [requested_at] datetimeoffset NOT NULL,
        [result_attachment_url] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_lab_orders] PRIMARY KEY ([lab_order_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [medicines] (
        [medicine_id] uniqueidentifier NOT NULL,
        [generic_name] nvarchar(450) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_medicines] PRIMARY KEY ([medicine_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [patient_documents] (
        [id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NULL,
        [file_name] nvarchar(max) NOT NULL,
        [file_size] bigint NULL,
        [file_content_type] nvarchar(max) NULL,
        [title] nvarchar(max) NULL,
        [description] nvarchar(max) NULL,
        [file_url] nvarchar(max) NOT NULL,
        [uploaded_by_user_id] uniqueidentifier NULL,
        [uploaded_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_patient_documents] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [patient_lab_results] (
        [id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NULL,
        [lab_order_id] uniqueidentifier NULL,
        [file_name] nvarchar(max) NOT NULL,
        [file_content_type] nvarchar(max) NULL,
        [result_title] nvarchar(max) NULL,
        [result_text] nvarchar(max) NULL,
        [status] nvarchar(max) NOT NULL,
        [file_url] nvarchar(max) NOT NULL,
        [uploaded_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_patient_lab_results] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [patient_vaccinations] (
        [id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NULL,
        [vaccine_name] nvarchar(max) NOT NULL,
        [manufacturer] nvarchar(max) NULL,
        [dose_number] smallint NULL,
        [route] nvarchar(max) NULL,
        [site] nvarchar(max) NULL,
        [lot_number] nvarchar(max) NULL,
        [expiry_date] date NULL,
        [administered_date] date NULL,
        [administered_by] uniqueidentifier NULL,
        [next_dose_date] date NULL,
        [status] nvarchar(max) NOT NULL,
        [source] nvarchar(max) NOT NULL,
        [notes] nvarchar(max) NULL,
        [reaction_notes] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_patient_vaccinations] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [patient_vital_readings] (
        [id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [template_id] uniqueidentifier NOT NULL,
        [value] nvarchar(max) NOT NULL,
        [recorded_at] date NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_patient_vital_readings] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [patients] (
        [patient_id] uniqueidentifier NOT NULL,
        [user_id] uniqueidentifier NULL,
        [patient_code] nvarchar(450) NOT NULL,
        [first_name] nvarchar(max) NOT NULL,
        [middle_name] nvarchar(max) NULL,
        [last_name] nvarchar(max) NOT NULL,
        [date_of_birth] date NOT NULL,
        [sex] nvarchar(max) NOT NULL,
        [civil_status] nvarchar(max) NULL,
        [address] nvarchar(max) NULL,
        [city] nvarchar(max) NULL,
        [zip_code] nvarchar(max) NULL,
        [contact_number] nvarchar(max) NULL,
        [email] nvarchar(max) NOT NULL,
        [emergency_contact_name] nvarchar(max) NULL,
        [emergency_contact_number] nvarchar(max) NULL,
        [emergency_contact_relationship] nvarchar(max) NULL,
        [blood_type] nvarchar(max) NULL,
        [philhealth_number] nvarchar(max) NULL,
        [hmo_provider] nvarchar(max) NULL,
        [hmo_card_number] nvarchar(max) NULL,
        [is_guest] bit NOT NULL,
        [is_email_verified] bit NOT NULL,
        [consented_at] datetimeoffset NULL,
        [consent_version] int NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_patients] PRIMARY KEY ([patient_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [prescription_groups] (
        [group_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_prescription_groups] PRIMARY KEY ([group_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [prescription_templates] (
        [template_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [title] nvarchar(max) NOT NULL,
        [is_system_template] bit NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_prescription_templates] PRIMARY KEY ([template_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [profiles] (
        [id] uniqueidentifier NOT NULL,
        [role] nvarchar(max) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_profiles] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [reviews] (
        [review_id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [rating] smallint NOT NULL,
        [comment] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_reviews] PRIMARY KEY ([review_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [services] (
        [service_id] uniqueidentifier NOT NULL,
        [name] nvarchar(max) NOT NULL,
        [category] nvarchar(max) NOT NULL,
        [description] nvarchar(max) NULL,
        [price] decimal(18,2) NOT NULL,
        [is_active] bit NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_services] PRIMARY KEY ([service_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [soap_phrases] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [field] nvarchar(max) NOT NULL,
        [label] nvarchar(max) NOT NULL,
        [body] nvarchar(max) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_soap_phrases] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [soap_templates] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [title] nvarchar(max) NOT NULL,
        [is_system_template] bit NOT NULL,
        [chief_complaint] nvarchar(max) NULL,
        [subjective] nvarchar(max) NULL,
        [objective] nvarchar(max) NULL,
        [assessment] nvarchar(max) NULL,
        [plan] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_soap_templates] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [staff_accounts] (
        [staff_id] uniqueidentifier NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [full_name] nvarchar(max) NOT NULL,
        [email] nvarchar(max) NOT NULL,
        [contact_number] nvarchar(max) NULL,
        [role] nvarchar(max) NOT NULL,
        [status] nvarchar(max) NOT NULL,
        [avatar_url] nvarchar(max) NULL,
        [invited_at] datetimeoffset NOT NULL,
        [revoked_at] datetimeoffset NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_staff_accounts] PRIMARY KEY ([staff_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [users] (
        [id] uniqueidentifier NOT NULL,
        [email] nvarchar(450) NOT NULL,
        [password_hash] nvarchar(max) NOT NULL,
        [email_confirmed] bit NOT NULL,
        [must_set_password] bit NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_users] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [vital_field_templates] (
        [template_id] uniqueidentifier NOT NULL,
        [description] nvarchar(max) NOT NULL,
        [form_key] nvarchar(450) NOT NULL,
        [unit] nvarchar(max) NOT NULL,
        [icon] nvarchar(max) NOT NULL,
        [is_default] bit NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_vital_field_templates] PRIMARY KEY ([template_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [prescription_line_items] (
        [id] uniqueidentifier NOT NULL,
        [group_id] uniqueidentifier NOT NULL,
        [medicine_id] uniqueidentifier NOT NULL,
        [generic_name] nvarchar(max) NOT NULL,
        [dosage] nvarchar(max) NOT NULL,
        [quantity] nvarchar(max) NOT NULL,
        [instruction] nvarchar(max) NULL,
        [is_controlled_substance] bit NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_prescription_line_items] PRIMARY KEY ([id]),
        CONSTRAINT [fk_prescription_line_items_prescription_groups_group_id] FOREIGN KEY ([group_id]) REFERENCES [prescription_groups] ([group_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [prescription_template_items] (
        [id] uniqueidentifier NOT NULL,
        [template_id] uniqueidentifier NOT NULL,
        [medicine_id] uniqueidentifier NOT NULL,
        [generic_name] nvarchar(max) NOT NULL,
        [dosage] nvarchar(max) NOT NULL,
        [quantity] nvarchar(max) NOT NULL,
        [instruction] nvarchar(max) NULL,
        [is_controlled_substance] bit NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_prescription_template_items] PRIMARY KEY ([id]),
        CONSTRAINT [fk_prescription_template_items_prescription_templates_template_id] FOREIGN KEY ([template_id]) REFERENCES [prescription_templates] ([template_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [doctors] (
        [doctor_id] uniqueidentifier NOT NULL,
        [specialization] nvarchar(max) NOT NULL,
        [consultation_fee] decimal(18,2) NOT NULL,
        [bio] nvarchar(max) NULL,
        [license_number] nvarchar(max) NULL,
        [ptr_number] nvarchar(max) NULL,
        [s2number] nvarchar(max) NULL,
        [slot_duration_minutes] int NOT NULL,
        [slot_capacity] int NOT NULL,
        [daily_patient_limit] int NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctors] PRIMARY KEY ([doctor_id]),
        CONSTRAINT [fk_doctors_staff_accounts_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [staff_accounts] ([staff_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [refresh_tokens] (
        [id] uniqueidentifier NOT NULL,
        [user_id] uniqueidentifier NOT NULL,
        [token_hash] nvarchar(450) NOT NULL,
        [expires_at] datetimeoffset NOT NULL,
        [revoked_at] datetimeoffset NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_refresh_tokens] PRIMARY KEY ([id]),
        CONSTRAINT [fk_refresh_tokens_users_user_id] FOREIGN KEY ([user_id]) REFERENCES [users] ([id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [bookings] (
        [booking_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [appointment_date] date NOT NULL,
        [slot_start_time] time NOT NULL,
        [slot_end_time] time NOT NULL,
        [status] nvarchar(max) NOT NULL,
        [payment_mode] nvarchar(max) NOT NULL,
        [queue_number] nvarchar(max) NULL,
        [consultation_fee_snapshot] decimal(18,2) NOT NULL,
        [total_fee] decimal(18,2) NOT NULL,
        [amount_due] decimal(18,2) NOT NULL,
        [is_walk_in] bit NOT NULL,
        [proof_type] nvarchar(max) NULL,
        [proof_value] nvarchar(max) NULL,
        [proof_submitted_at] datetimeoffset NULL,
        [cancelled_by_user_id] uniqueidentifier NULL,
        [cancellation_reason] nvarchar(max) NULL,
        [notes] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_bookings] PRIMARY KEY ([booking_id]),
        CONSTRAINT [fk_bookings_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]) ON DELETE NO ACTION,
        CONSTRAINT [fk_bookings_patients_patient_id] FOREIGN KEY ([patient_id]) REFERENCES [patients] ([patient_id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [doctor_blocked_dates] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [blocked_date] date NOT NULL,
        [reason] nvarchar(max) NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctor_blocked_dates] PRIMARY KEY ([id]),
        CONSTRAINT [fk_doctor_blocked_dates_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [doctor_day_statuses] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [status_date] date NOT NULL,
        [status] nvarchar(max) NOT NULL,
        [running_late_minutes] int NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctor_day_statuses] PRIMARY KEY ([id]),
        CONSTRAINT [fk_doctor_day_statuses_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [doctor_schedules] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [day_of_week] smallint NOT NULL,
        [is_active] bit NOT NULL,
        [start_time] time NOT NULL,
        [end_time] time NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctor_schedules] PRIMARY KEY ([id]),
        CONSTRAINT [fk_doctor_schedules_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [doctor_services] (
        [doctor_id] uniqueidentifier NOT NULL,
        [service_id] uniqueidentifier NOT NULL,
        [duration_minutes] int NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctor_services] PRIMARY KEY ([doctor_id], [service_id]),
        CONSTRAINT [fk_doctor_services_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]) ON DELETE CASCADE,
        CONSTRAINT [fk_doctor_services_services_service_id] FOREIGN KEY ([service_id]) REFERENCES [services] ([service_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [booking_services] (
        [booking_id] uniqueidentifier NOT NULL,
        [service_id] uniqueidentifier NOT NULL,
        [price_at_booking] decimal(18,2) NOT NULL,
        CONSTRAINT [pk_booking_services] PRIMARY KEY ([booking_id], [service_id]),
        CONSTRAINT [fk_booking_services_bookings_booking_id] FOREIGN KEY ([booking_id]) REFERENCES [bookings] ([booking_id]) ON DELETE CASCADE,
        CONSTRAINT [fk_booking_services_services_service_id] FOREIGN KEY ([service_id]) REFERENCES [services] ([service_id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [consultations] (
        [consultation_id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [status] nvarchar(max) NOT NULL,
        [chief_complaint] nvarchar(max) NULL,
        [subjective] nvarchar(max) NULL,
        [objective] nvarchar(max) NULL,
        [assessment] nvarchar(max) NULL,
        [plan] nvarchar(max) NULL,
        [doctor_notes] nvarchar(max) NULL,
        [completed_by_user_id] uniqueidentifier NULL,
        [completed_at] datetimeoffset NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_consultations] PRIMARY KEY ([consultation_id]),
        CONSTRAINT [fk_consultations_bookings_booking_id] FOREIGN KEY ([booking_id]) REFERENCES [bookings] ([booking_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE TABLE [payments] (
        [payment_id] uniqueidentifier NOT NULL,
        [booking_id] uniqueidentifier NOT NULL,
        [amount] decimal(18,2) NOT NULL,
        [status] nvarchar(max) NOT NULL,
        [payment_method] nvarchar(max) NULL,
        [reference_number] nvarchar(max) NULL,
        [or_number] nvarchar(max) NULL,
        [amount_received] decimal(18,2) NULL,
        [confirm_notes] nvarchar(max) NULL,
        [confirmed_by_user_id] uniqueidentifier NULL,
        [confirmed_at] datetimeoffset NULL,
        [waived_by_user_id] uniqueidentifier NULL,
        [waived_reason] nvarchar(max) NULL,
        [waived_at] datetimeoffset NULL,
        [refunded_by_user_id] uniqueidentifier NULL,
        [refund_amount] decimal(18,2) NULL,
        [refund_reason] nvarchar(max) NULL,
        [refunded_at] datetimeoffset NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_payments] PRIMARY KEY ([payment_id]),
        CONSTRAINT [fk_payments_bookings_booking_id] FOREIGN KEY ([booking_id]) REFERENCES [bookings] ([booking_id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'payment_method') AND [object_id] = OBJECT_ID(N'[clinic_accepted_payment_methods]'))
        SET IDENTITY_INSERT [clinic_accepted_payment_methods] ON;
    EXEC(N'INSERT INTO [clinic_accepted_payment_methods] ([payment_method])
    VALUES (N''BankTransfer''),
    (N''Cash''),
    (N''GCash''),
    (N''Maya'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'payment_method') AND [object_id] = OBJECT_ID(N'[clinic_accepted_payment_methods]'))
        SET IDENTITY_INSERT [clinic_accepted_payment_methods] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'day_of_week', N'close_time', N'is_closed', N'open_time') AND [object_id] = OBJECT_ID(N'[clinic_operating_hours]'))
        SET IDENTITY_INSERT [clinic_operating_hours] ON;
    EXEC(N'INSERT INTO [clinic_operating_hours] ([day_of_week], [close_time], [is_closed], [open_time])
    VALUES (CAST(0 AS smallint), NULL, CAST(1 AS bit), NULL),
    (CAST(1 AS smallint), ''17:00:00'', CAST(0 AS bit), ''08:00:00''),
    (CAST(2 AS smallint), ''17:00:00'', CAST(0 AS bit), ''08:00:00''),
    (CAST(3 AS smallint), ''17:00:00'', CAST(0 AS bit), ''08:00:00''),
    (CAST(4 AS smallint), ''17:00:00'', CAST(0 AS bit), ''08:00:00''),
    (CAST(5 AS smallint), ''17:00:00'', CAST(0 AS bit), ''08:00:00''),
    (CAST(6 AS smallint), ''12:00:00'', CAST(0 AS bit), ''08:00:00'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'day_of_week', N'close_time', N'is_closed', N'open_time') AND [object_id] = OBJECT_ID(N'[clinic_operating_hours]'))
        SET IDENTITY_INSERT [clinic_operating_hours] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'address', N'clinic_name', N'consent_version', N'contact_number', N'default_payment_mode', N'description', N'email', N'favicon_url', N'logo_url', N'primary_color', N'privacy_policy_text', N'refund_policy', N'secondary_color', N'updated_at', N'updated_by_user_id', N'website_url') AND [object_id] = OBJECT_ID(N'[clinic_settings]'))
        SET IDENTITY_INSERT [clinic_settings] ON;
    EXEC(N'INSERT INTO [clinic_settings] ([id], [address], [clinic_name], [consent_version], [contact_number], [default_payment_mode], [description], [email], [favicon_url], [logo_url], [primary_color], [privacy_policy_text], [refund_policy], [secondary_color], [updated_at], [updated_by_user_id], [website_url])
    VALUES (CAST(1 AS smallint), N''TBD'', N''Dr. Grace Gavino Medical Clinic'', 1, NULL, N''PayAtClinic'', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, ''2026-01-01T00:00:00.0000000+00:00'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'id', N'address', N'clinic_name', N'consent_version', N'contact_number', N'default_payment_mode', N'description', N'email', N'favicon_url', N'logo_url', N'primary_color', N'privacy_policy_text', N'refund_policy', N'secondary_color', N'updated_at', N'updated_by_user_id', N'website_url') AND [object_id] = OBJECT_ID(N'[clinic_settings]'))
        SET IDENTITY_INSERT [clinic_settings] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'code', N'description') AND [object_id] = OBJECT_ID(N'[icd10codes]'))
        SET IDENTITY_INSERT [icd10codes] ON;
    EXEC(N'INSERT INTO [icd10codes] ([code], [description])
    VALUES (N''E11.9'', N''Type 2 diabetes mellitus without complications''),
    (N''I10'', N''Essential (primary) hypertension''),
    (N''J06.9'', N''Acute upper respiratory infection, unspecified''),
    (N''J45.909'', N''Unspecified asthma, uncomplicated''),
    (N''Z00.0'', N''General health examination'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'code', N'description') AND [object_id] = OBJECT_ID(N'[icd10codes]'))
        SET IDENTITY_INSERT [icd10codes] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'medicine_id', N'created_at', N'generic_name') AND [object_id] = OBJECT_ID(N'[medicines]'))
        SET IDENTITY_INSERT [medicines] ON;
    EXEC(N'INSERT INTO [medicines] ([medicine_id], [created_at], [generic_name])
    VALUES (''22222222-0000-0000-0000-000000000001'', ''2026-01-01T00:00:00.0000000+00:00'', N''PARACETAMOL 500MG TAB''),
    (''22222222-0000-0000-0000-000000000002'', ''2026-01-01T00:00:00.0000000+00:00'', N''AMOXICILLIN + CLAVULANIC ACID (CO-AMOXICLAV) 500MG TAB''),
    (''22222222-0000-0000-0000-000000000003'', ''2026-01-01T00:00:00.0000000+00:00'', N''CEFIXIME 200MG TAB''),
    (''22222222-0000-0000-0000-000000000004'', ''2026-01-01T00:00:00.0000000+00:00'', N''LOPERAMIDE 2MG CAP''),
    (''22222222-0000-0000-0000-000000000005'', ''2026-01-01T00:00:00.0000000+00:00'', N''MEFENAMIC ACID 500MG TAB''),
    (''22222222-0000-0000-0000-000000000006'', ''2026-01-01T00:00:00.0000000+00:00'', N''CETIRIZINE 10MG TAB''),
    (''22222222-0000-0000-0000-000000000007'', ''2026-01-01T00:00:00.0000000+00:00'', N''OMEPRAZOLE 20MG CAP''),
    (''22222222-0000-0000-0000-000000000008'', ''2026-01-01T00:00:00.0000000+00:00'', N''METFORMIN 500MG TAB''),
    (''22222222-0000-0000-0000-000000000009'', ''2026-01-01T00:00:00.0000000+00:00'', N''LOSARTAN 50MG TAB''),
    (''22222222-0000-0000-0000-000000000010'', ''2026-01-01T00:00:00.0000000+00:00'', N''ASCORBIC ACID + MULTIVITAMINS 500MG TAB''),
    (''22222222-0000-0000-0000-000000000011'', ''2026-01-01T00:00:00.0000000+00:00'', N''SALBUTAMOL 2MG/5ML SYRUP''),
    (''22222222-0000-0000-0000-000000000012'', ''2026-01-01T00:00:00.0000000+00:00'', N''AMLODIPINE 5MG TAB''),
    (''22222222-0000-0000-0000-000000000013'', ''2026-01-01T00:00:00.0000000+00:00'', N''AZITHROMYCIN 500MG TAB''),
    (''22222222-0000-0000-0000-000000000014'', ''2026-01-01T00:00:00.0000000+00:00'', N''IBUPROFEN 400MG TAB''),
    (''22222222-0000-0000-0000-000000000015'', ''2026-01-01T00:00:00.0000000+00:00'', N''SIMVASTATIN 20MG TAB'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'medicine_id', N'created_at', N'generic_name') AND [object_id] = OBJECT_ID(N'[medicines]'))
        SET IDENTITY_INSERT [medicines] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'template_id', N'created_at', N'description', N'form_key', N'icon', N'is_default', N'unit') AND [object_id] = OBJECT_ID(N'[vital_field_templates]'))
        SET IDENTITY_INSERT [vital_field_templates] ON;
    EXEC(N'INSERT INTO [vital_field_templates] ([template_id], [created_at], [description], [form_key], [icon], [is_default], [unit])
    VALUES (''11111111-0000-0000-0000-000000000001'', ''2026-01-01T00:00:00.0000000+00:00'', N''TEMPERATURE'', N''temperature'', N''thermometer'', CAST(1 AS bit), N''°C''),
    (''11111111-0000-0000-0000-000000000002'', ''2026-01-01T00:00:00.0000000+00:00'', N''PULSE RATE'', N''pulse_rate'', N''heart_pulse'', CAST(1 AS bit), N''bpm''),
    (''11111111-0000-0000-0000-000000000003'', ''2026-01-01T00:00:00.0000000+00:00'', N''RESPIRATORY RATE'', N''respiratory_rate'', N''lungs'', CAST(1 AS bit), N''rpm''),
    (''11111111-0000-0000-0000-000000000004'', ''2026-01-01T00:00:00.0000000+00:00'', N''BLOOD PRESSURE'', N''blood_pressure'', N''gauge'', CAST(1 AS bit), N''mmHg''),
    (''11111111-0000-0000-0000-000000000005'', ''2026-01-01T00:00:00.0000000+00:00'', N''O2 SATURATION'', N''o2_saturation'', N''wind'', CAST(1 AS bit), N''%''),
    (''11111111-0000-0000-0000-000000000006'', ''2026-01-01T00:00:00.0000000+00:00'', N''HEIGHT'', N''height'', N''ruler'', CAST(1 AS bit), N''cm''),
    (''11111111-0000-0000-0000-000000000007'', ''2026-01-01T00:00:00.0000000+00:00'', N''WEIGHT'', N''weight'', N''weight_scale'', CAST(1 AS bit), N''kg''),
    (''11111111-0000-0000-0000-000000000008'', ''2026-01-01T00:00:00.0000000+00:00'', N''Fundal Height'', N''fundal_height'', N''ruler'', CAST(0 AS bit), N''''),
    (''11111111-0000-0000-0000-000000000009'', ''2026-01-01T00:00:00.0000000+00:00'', N''Fetal Heart Rate'', N''fetal_heart_rate'', N''heart_pulse'', CAST(0 AS bit), N'''')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'template_id', N'created_at', N'description', N'form_key', N'icon', N'is_default', N'unit') AND [object_id] = OBJECT_ID(N'[vital_field_templates]'))
        SET IDENTITY_INSERT [vital_field_templates] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_booking_services_service_id] ON [booking_services] ([service_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_bookings_doctor_id] ON [bookings] ([doctor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_bookings_patient_id] ON [bookings] ([patient_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_consultations_booking_id] ON [consultations] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_doctor_blocked_dates_doctor_id] ON [doctor_blocked_dates] ([doctor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_doctor_day_statuses_doctor_id_status_date] ON [doctor_day_statuses] ([doctor_id], [status_date]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_doctor_schedules_doctor_id_day_of_week] ON [doctor_schedules] ([doctor_id], [day_of_week]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_doctor_services_service_id] ON [doctor_services] ([service_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_follow_ups_consultation_id] ON [follow_ups] ([consultation_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_medicines_generic_name] ON [medicines] ([generic_name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_patient_vital_readings_booking_id_template_id] ON [patient_vital_readings] ([booking_id], [template_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_patients_patient_code] ON [patients] ([patient_code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_payments_booking_id] ON [payments] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_prescription_line_items_group_id] ON [prescription_line_items] ([group_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_prescription_template_items_template_id] ON [prescription_template_items] ([template_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_refresh_tokens_token_hash] ON [refresh_tokens] ([token_hash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE INDEX [ix_refresh_tokens_user_id] ON [refresh_tokens] ([user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_users_email] ON [users] ([email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [ix_vital_field_templates_form_key] ON [vital_field_templates] ([form_key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN

    CREATE VIEW v_unpaid_completed_visits AS
    SELECT
      b.booking_id,
      b.patient_id,
      p.patient_code,
      p.first_name + ' ' + p.last_name AS patient_name,
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
    WHERE b.status = 'Completed' AND pay.status = 'Unpaid';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN

    CREATE VIEW v_pending_follow_ups AS
    SELECT
      f.id AS follow_up_id,
      f.patient_id,
      p.first_name + ' ' + p.last_name AS patient_name,
      f.doctor_id,
      sa.full_name AS doctor_name,
      f.follow_up_date,
      f.reason,
      f.status
    FROM follow_ups f
    JOIN patients p ON p.patient_id = f.patient_id
    JOIN doctors d ON d.doctor_id = f.doctor_id
    JOIN staff_accounts sa ON sa.staff_id = d.doctor_id
    WHERE f.status = 'Pending';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN

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
    GROUP BY b.appointment_date;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN

    CREATE VIEW v_doctor_ratings AS
    SELECT
      d.doctor_id,
      COALESCE(ROUND(AVG(CAST(r.rating AS DECIMAL(18,2))), 2), 0) AS average_rating,
      COUNT(r.review_id) AS review_count
    FROM doctors d
    LEFT JOIN reviews r ON r.doctor_id = d.doctor_id
    GROUP BY d.doctor_id;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260907114641_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260907114641_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909070207_RenameIcd10CodesTable'
)
BEGIN
    ALTER TABLE [icd10codes] DROP CONSTRAINT [pk_icd10codes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909070207_RenameIcd10CodesTable'
)
BEGIN
    EXEC sp_rename N'[icd10codes]', N'icd10_codes', 'OBJECT';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909070207_RenameIcd10CodesTable'
)
BEGIN
    ALTER TABLE [icd10_codes] ADD CONSTRAINT [pk_icd10_codes] PRIMARY KEY ([code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909070207_RenameIcd10CodesTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909070207_RenameIcd10CodesTable', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909091846_FixS2NumberColumnName'
)
BEGIN
    EXEC sp_rename N'[doctors].[s2number]', N's2_number', 'COLUMN';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909091846_FixS2NumberColumnName'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909091846_FixS2NumberColumnName', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909094030_AddUserIdUniqueIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [ix_staff_accounts_user_id] ON [staff_accounts] ([user_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909094030_AddUserIdUniqueIndexes'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [ix_patients_user_id] ON [patients] ([user_id]) WHERE [user_id] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909094030_AddUserIdUniqueIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909094030_AddUserIdUniqueIndexes', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    CREATE INDEX [ix_prescription_groups_booking_id] ON [prescription_groups] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    CREATE INDEX [ix_consultations_doctor_id] ON [consultations] ([doctor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    CREATE INDEX [ix_consultation_diagnoses_consultation_id] ON [consultation_diagnoses] ([consultation_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    ALTER TABLE [consultation_diagnoses] ADD CONSTRAINT [fk_consultation_diagnoses_consultations_consultation_id] FOREIGN KEY ([consultation_id]) REFERENCES [consultations] ([consultation_id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    ALTER TABLE [consultations] ADD CONSTRAINT [fk_consultations_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    ALTER TABLE [follow_ups] ADD CONSTRAINT [fk_follow_ups_consultations_consultation_id] FOREIGN KEY ([consultation_id]) REFERENCES [consultations] ([consultation_id]) ON DELETE CASCADE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    ALTER TABLE [prescription_groups] ADD CONSTRAINT [fk_prescription_groups_bookings_booking_id] FOREIGN KEY ([booking_id]) REFERENCES [bookings] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909133513_ClinicalNavigations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909133513_ClinicalNavigations', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909135915_PatientFileBookingNav'
)
BEGIN
    CREATE INDEX [ix_patient_lab_results_booking_id] ON [patient_lab_results] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909135915_PatientFileBookingNav'
)
BEGIN
    CREATE INDEX [ix_patient_documents_booking_id] ON [patient_documents] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909135915_PatientFileBookingNav'
)
BEGIN
    ALTER TABLE [patient_documents] ADD CONSTRAINT [fk_patient_documents_bookings_booking_id] FOREIGN KEY ([booking_id]) REFERENCES [bookings] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909135915_PatientFileBookingNav'
)
BEGIN
    ALTER TABLE [patient_lab_results] ADD CONSTRAINT [fk_patient_lab_results_bookings_booking_id] FOREIGN KEY ([booking_id]) REFERENCES [bookings] ([booking_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909135915_PatientFileBookingNav'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909135915_PatientFileBookingNav', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [patients] ADD [pwd_id_number] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [patients] ADD [senior_id_number] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [clinic_settings] ADD [discount_pct] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [clinic_settings] ADD [fee_consultation] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [clinic_settings] ADD [fee_follow_up] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [clinic_settings] ADD [fee_med_cert] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [clinic_settings] ADD [fee_senior_pwd] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [bookings] ADD [discount_amount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [bookings] ADD [discount_category] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [bookings] ADD [med_cert_requested] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    ALTER TABLE [bookings] ADD [visit_type] nvarchar(max) NOT NULL DEFAULT N'New';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    EXEC(N'UPDATE [clinic_operating_hours] SET [close_time] = ''17:00:00'', [open_time] = ''10:00:00''
    WHERE [day_of_week] = CAST(6 AS smallint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    EXEC(N'UPDATE [clinic_settings] SET [address] = N''3ML Quezon National Highway, Buaya, Lapu-Lapu City'', [clinic_name] = N''Grace Medical Clinic'', [contact_number] = N''09285612976'', [discount_pct] = 0.2, [fee_consultation] = 450.0, [fee_follow_up] = 350.0, [fee_med_cert] = 50.0, [fee_senior_pwd] = 400.0
    WHERE [id] = CAST(1 AS smallint);
    SELECT @@ROWCOUNT');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144236_Phase8FeeScheduleAndVisitType'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909144236_Phase8FeeScheduleAndVisitType', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144609_Phase81VitalsRecordedBy'
)
BEGIN
    ALTER TABLE [patient_vital_readings] ADD [recorded_by_user_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909144609_Phase81VitalsRecordedBy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909144609_Phase81VitalsRecordedBy', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150308_Phase87DoctorEarningsView'
)
BEGIN

    CREATE VIEW v_doctor_earnings AS
    SELECT
      b.doctor_id,
      CONVERT(char(7), b.appointment_date, 126) AS period,
      COUNT(DISTINCT b.booking_id) AS completed_visits,
      COALESCE(SUM(b.total_fee), 0) AS gross_billed,
      COALESCE(SUM(CASE WHEN pay.status = 'Paid' THEN pay.amount ELSE 0 END), 0) AS collected,
      COALESCE(SUM(CASE WHEN pay.status = 'Waived' THEN pay.amount ELSE 0 END), 0) AS waived
    FROM bookings b
    LEFT JOIN payments pay ON pay.booking_id = b.booking_id
    WHERE b.status = 'Completed'
    GROUP BY b.doctor_id, CONVERT(char(7), b.appointment_date, 126);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150308_Phase87DoctorEarningsView'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909150308_Phase87DoctorEarningsView', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150641_Phase85MedicalCertificates'
)
BEGIN
    CREATE TABLE [medical_certificates] (
        [certificate_id] uniqueidentifier NOT NULL,
        [consultation_id] uniqueidentifier NOT NULL,
        [patient_id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [issue_date] date NOT NULL,
        [patient_address_snapshot] nvarchar(max) NULL,
        [examined_at] nvarchar(max) NULL,
        [examination_date_from] date NULL,
        [examination_date_to] date NULL,
        [diagnosis_text] nvarchar(max) NULL,
        [recommendations] nvarchar(max) NULL,
        [purpose_exception] nvarchar(max) NULL,
        [come_back_on] date NULL,
        [issued_by_user_id] uniqueidentifier NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_medical_certificates] PRIMARY KEY ([certificate_id]),
        CONSTRAINT [fk_medical_certificates_consultations_consultation_id] FOREIGN KEY ([consultation_id]) REFERENCES [consultations] ([consultation_id]) ON DELETE CASCADE,
        CONSTRAINT [fk_medical_certificates_doctors_doctor_id] FOREIGN KEY ([doctor_id]) REFERENCES [doctors] ([doctor_id]),
        CONSTRAINT [fk_medical_certificates_patients_patient_id] FOREIGN KEY ([patient_id]) REFERENCES [patients] ([patient_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150641_Phase85MedicalCertificates'
)
BEGIN
    CREATE UNIQUE INDEX [ix_medical_certificates_consultation_id] ON [medical_certificates] ([consultation_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150641_Phase85MedicalCertificates'
)
BEGIN
    CREATE INDEX [ix_medical_certificates_doctor_id] ON [medical_certificates] ([doctor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150641_Phase85MedicalCertificates'
)
BEGIN
    CREATE INDEX [ix_medical_certificates_patient_id] ON [medical_certificates] ([patient_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909150641_Phase85MedicalCertificates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909150641_Phase85MedicalCertificates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [prescription_line_items] ADD [duration_kind] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [prescription_line_items] ADD [duration_value] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [prescription_line_items] ADD [indication] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [prescription_line_items] ADD [meal_relation] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [prescription_line_items] ADD [timing] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [lab_orders] ADD [lab_test_id] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    CREATE TABLE [lab_test_catalog] (
        [lab_test_id] uniqueidentifier NOT NULL,
        [name] nvarchar(450) NOT NULL,
        [is_default] bit NOT NULL,
        [sort_order] int NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_lab_test_catalog] PRIMARY KEY ([lab_test_id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'lab_test_id', N'created_at', N'is_default', N'name', N'sort_order') AND [object_id] = OBJECT_ID(N'[lab_test_catalog]'))
        SET IDENTITY_INSERT [lab_test_catalog] ON;
    EXEC(N'INSERT INTO [lab_test_catalog] ([lab_test_id], [created_at], [is_default], [name], [sort_order])
    VALUES (''33333333-0000-0000-0000-000000000001'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''CBC'', 1),
    (''33333333-0000-0000-0000-000000000002'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''URINALYSIS'', 2),
    (''33333333-0000-0000-0000-000000000003'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''LIPID PROFILE'', 3),
    (''33333333-0000-0000-0000-000000000004'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''SGPT'', 4),
    (''33333333-0000-0000-0000-000000000005'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''CREA'', 5),
    (''33333333-0000-0000-0000-000000000006'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''FBS'', 6),
    (''33333333-0000-0000-0000-000000000007'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''BUA'', 7),
    (''33333333-0000-0000-0000-000000000008'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''DENGUE NS1/IgG/IgM'', 8),
    (''33333333-0000-0000-0000-000000000009'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(1 AS bit), N''ECG 12L'', 9),
    (''33333333-0000-0000-0000-000000000010'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(0 AS bit), N''TSH'', 10),
    (''33333333-0000-0000-0000-000000000011'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(0 AS bit), N''FT3'', 11),
    (''33333333-0000-0000-0000-000000000012'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(0 AS bit), N''FT4'', 12),
    (''33333333-0000-0000-0000-000000000013'', ''2026-01-01T00:00:00.0000000+00:00'', CAST(0 AS bit), N''HbA1c'', 13)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'lab_test_id', N'created_at', N'is_default', N'name', N'sort_order') AND [object_id] = OBJECT_ID(N'[lab_test_catalog]'))
        SET IDENTITY_INSERT [lab_test_catalog] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    CREATE INDEX [ix_lab_orders_lab_test_id] ON [lab_orders] ([lab_test_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    CREATE UNIQUE INDEX [ix_lab_test_catalog_name] ON [lab_test_catalog] ([name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    ALTER TABLE [lab_orders] ADD CONSTRAINT [fk_lab_orders_lab_test_catalog_lab_test_id] FOREIGN KEY ([lab_test_id]) REFERENCES [lab_test_catalog] ([lab_test_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260909151159_Phase85LabCatalogAndRxFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260909151159_Phase85LabCatalogAndRxFields', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910060305_Phase91PatientCodeSequence'
)
BEGIN

    IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'patient_code_seq')
        CREATE SEQUENCE patient_code_seq AS bigint START WITH 100000 INCREMENT BY 1 NO CYCLE;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910060305_Phase91PatientCodeSequence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260910060305_Phase91PatientCodeSequence', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910091238_Phase92DoctorDiagnosisTemplates'
)
BEGIN
    CREATE TABLE [doctor_diagnosis_templates] (
        [id] uniqueidentifier NOT NULL,
        [doctor_id] uniqueidentifier NOT NULL,
        [label] nvarchar(max) NOT NULL,
        [body] nvarchar(max) NOT NULL,
        [created_at] datetimeoffset NOT NULL,
        [updated_at] datetimeoffset NOT NULL,
        CONSTRAINT [pk_doctor_diagnosis_templates] PRIMARY KEY ([id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910091238_Phase92DoctorDiagnosisTemplates'
)
BEGIN
    CREATE INDEX [ix_doctor_diagnosis_templates_doctor_id] ON [doctor_diagnosis_templates] ([doctor_id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910091238_Phase92DoctorDiagnosisTemplates'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260910091238_Phase92DoctorDiagnosisTemplates', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910130827_Phase93ConsultationPfDecision'
)
BEGIN
    ALTER TABLE [consultations] ADD [pf_amount] decimal(18,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910130827_Phase93ConsultationPfDecision'
)
BEGIN
    ALTER TABLE [consultations] ADD [pf_decision] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910130827_Phase93ConsultationPfDecision'
)
BEGIN
    ALTER TABLE [consultations] ADD [pf_waive_reason] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [migration_id] = N'20260910130827_Phase93ConsultationPfDecision'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([migration_id], [product_version])
    VALUES (N'20260910130827_Phase93ConsultationPfDecision', N'10.0.11');
END;

COMMIT;
GO

