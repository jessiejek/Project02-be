using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    posted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    performed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    details = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    performed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "clinic_accepted_payment_methods",
                columns: table => new
                {
                    payment_method = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_accepted_payment_methods", x => x.payment_method);
                });

            migrationBuilder.CreateTable(
                name: "clinic_operating_hours",
                columns: table => new
                {
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    is_closed = table.Column<bool>(type: "bit", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time", nullable: true),
                    close_time = table.Column<TimeOnly>(type: "time", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_operating_hours", x => x.day_of_week);
                });

            migrationBuilder.CreateTable(
                name: "clinic_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    clinic_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    address = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    contact_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    default_payment_mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    refund_policy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    consent_version = table.Column<int>(type: "int", nullable: false),
                    primary_color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    secondary_color = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    logo_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    favicon_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    website_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    privacy_policy_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clinic_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "consultation_diagnoses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    icd10code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    custom_description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultation_diagnoses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "doctor_favorite_medicines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    medicine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generic_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dosage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    quantity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    instruction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_favorite_medicines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "follow_ups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    follow_up_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reminder_enabled = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_ups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "icd10codes",
                columns: table => new
                {
                    code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_icd10codes", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "lab_orders",
                columns: table => new
                {
                    lab_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    test_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    test_code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    clinical_indication = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    specimen_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    result_attachment_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_orders", x => x.lab_order_id);
                });

            migrationBuilder.CreateTable(
                name: "medicines",
                columns: table => new
                {
                    medicine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generic_name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medicines", x => x.medicine_id);
                });

            migrationBuilder.CreateTable(
                name: "patient_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    file_content_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    file_url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patient_lab_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    lab_order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    file_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    file_content_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    result_title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    result_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    file_url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_lab_results", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patient_vaccinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    vaccine_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    manufacturer = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    dose_number = table.Column<short>(type: "smallint", nullable: true),
                    route = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    site = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    lot_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    administered_date = table.Column<DateOnly>(type: "date", nullable: true),
                    administered_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    next_dose_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reaction_notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_vaccinations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patient_vital_readings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    recorded_at = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_vital_readings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patients",
                columns: table => new
                {
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    patient_code = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    middle_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    last_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    sex = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    civil_status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    city = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    zip_code = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    contact_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    emergency_contact_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    emergency_contact_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    emergency_contact_relationship = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    blood_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    philhealth_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hmo_provider = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    hmo_card_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_guest = table.Column<bool>(type: "bit", nullable: false),
                    is_email_verified = table.Column<bool>(type: "bit", nullable: false),
                    consented_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    consent_version = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.patient_id);
                });

            migrationBuilder.CreateTable(
                name: "prescription_groups",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescription_groups", x => x.group_id);
                });

            migrationBuilder.CreateTable(
                name: "prescription_templates",
                columns: table => new
                {
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_system_template = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescription_templates", x => x.template_id);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    review_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.review_id);
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    service_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.service_id);
                });

            migrationBuilder.CreateTable(
                name: "soap_phrases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soap_phrases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "soap_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_system_template = table.Column<bool>(type: "bit", nullable: false),
                    chief_complaint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    subjective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    objective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    assessment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    plan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soap_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "staff_accounts",
                columns: table => new
                {
                    staff_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    contact_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    avatar_url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    invited_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_accounts", x => x.staff_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    must_set_password = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vital_field_templates",
                columns: table => new
                {
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    form_key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    unit = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    icon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_default = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vital_field_templates", x => x.template_id);
                });

            migrationBuilder.CreateTable(
                name: "prescription_line_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    medicine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generic_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dosage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    quantity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    instruction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_controlled_substance = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescription_line_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_prescription_line_items_prescription_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "prescription_groups",
                        principalColumn: "group_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prescription_template_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    medicine_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generic_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    dosage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    quantity = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    instruction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_controlled_substance = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescription_template_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_prescription_template_items_prescription_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "prescription_templates",
                        principalColumn: "template_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "doctors",
                columns: table => new
                {
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    specialization = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    consultation_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    bio = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    license_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ptr_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    s2number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    slot_duration_minutes = table.Column<int>(type: "int", nullable: false),
                    slot_capacity = table.Column<int>(type: "int", nullable: false),
                    daily_patient_limit = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctors", x => x.doctor_id);
                    table.ForeignKey(
                        name: "fk_doctors_staff_accounts_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "staff_accounts",
                        principalColumn: "staff_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    appointment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    slot_start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    slot_end_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    payment_mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    queue_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    consultation_fee_snapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    total_fee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    amount_due = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    is_walk_in = table.Column<bool>(type: "bit", nullable: false),
                    proof_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    proof_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    proof_submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancellation_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.booking_id);
                    table.ForeignKey(
                        name: "fk_bookings_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "doctor_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "patient_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_blocked_dates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    blocked_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_blocked_dates", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_blocked_dates_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "doctor_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "doctor_day_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    running_late_minutes = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_day_statuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_day_statuses_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "doctor_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "doctor_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    day_of_week = table.Column<short>(type: "smallint", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_schedules_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "doctor_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "doctor_services",
                columns: table => new
                {
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    duration_minutes = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_services", x => new { x.doctor_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_doctor_services_doctors_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "doctors",
                        principalColumn: "doctor_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_doctor_services_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "booking_services",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    service_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    price_at_booking = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_services", x => new { x.booking_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_booking_services_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_booking_services_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "service_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "consultations",
                columns: table => new
                {
                    consultation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    patient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    chief_complaint = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    subjective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    objective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    assessment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    plan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    doctor_notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consultations", x => x.consultation_id);
                    table.ForeignKey(
                        name: "fk_consultations_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    payment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    booking_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    payment_method = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    reference_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    or_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    amount_received = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    confirm_notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    waived_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    waived_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    waived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    refunded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    refund_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    refund_reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.payment_id);
                    table.ForeignKey(
                        name: "fk_payments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "booking_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "clinic_accepted_payment_methods",
                column: "payment_method",
                values: new object[]
                {
                    "BankTransfer",
                    "Cash",
                    "GCash",
                    "Maya"
                });

            migrationBuilder.InsertData(
                table: "clinic_operating_hours",
                columns: new[] { "day_of_week", "close_time", "is_closed", "open_time" },
                values: new object[,]
                {
                    { (short)0, null, true, null },
                    { (short)1, new TimeOnly(17, 0, 0), false, new TimeOnly(8, 0, 0) },
                    { (short)2, new TimeOnly(17, 0, 0), false, new TimeOnly(8, 0, 0) },
                    { (short)3, new TimeOnly(17, 0, 0), false, new TimeOnly(8, 0, 0) },
                    { (short)4, new TimeOnly(17, 0, 0), false, new TimeOnly(8, 0, 0) },
                    { (short)5, new TimeOnly(17, 0, 0), false, new TimeOnly(8, 0, 0) },
                    { (short)6, new TimeOnly(12, 0, 0), false, new TimeOnly(8, 0, 0) }
                });

            migrationBuilder.InsertData(
                table: "clinic_settings",
                columns: new[] { "id", "address", "clinic_name", "consent_version", "contact_number", "default_payment_mode", "description", "email", "favicon_url", "logo_url", "primary_color", "privacy_policy_text", "refund_policy", "secondary_color", "updated_at", "updated_by_user_id", "website_url" },
                values: new object[] { (short)1, "TBD", "Dr. Grace Gavino Medical Clinic", 1, null, "PayAtClinic", null, null, null, null, null, null, null, null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null });

            migrationBuilder.InsertData(
                table: "icd10codes",
                columns: new[] { "code", "description" },
                values: new object[,]
                {
                    { "E11.9", "Type 2 diabetes mellitus without complications" },
                    { "I10", "Essential (primary) hypertension" },
                    { "J06.9", "Acute upper respiratory infection, unspecified" },
                    { "J45.909", "Unspecified asthma, uncomplicated" },
                    { "Z00.0", "General health examination" }
                });

            migrationBuilder.InsertData(
                table: "medicines",
                columns: new[] { "medicine_id", "created_at", "generic_name" },
                values: new object[,]
                {
                    { new Guid("22222222-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PARACETAMOL 500MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "AMOXICILLIN + CLAVULANIC ACID (CO-AMOXICLAV) 500MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "CEFIXIME 200MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "LOPERAMIDE 2MG CAP" },
                    { new Guid("22222222-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "MEFENAMIC ACID 500MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "CETIRIZINE 10MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "OMEPRAZOLE 20MG CAP" },
                    { new Guid("22222222-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "METFORMIN 500MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "LOSARTAN 50MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000010"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "ASCORBIC ACID + MULTIVITAMINS 500MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000011"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SALBUTAMOL 2MG/5ML SYRUP" },
                    { new Guid("22222222-0000-0000-0000-000000000012"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "AMLODIPINE 5MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000013"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "AZITHROMYCIN 500MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000014"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "IBUPROFEN 400MG TAB" },
                    { new Guid("22222222-0000-0000-0000-000000000015"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "SIMVASTATIN 20MG TAB" }
                });

            migrationBuilder.InsertData(
                table: "vital_field_templates",
                columns: new[] { "template_id", "created_at", "description", "form_key", "icon", "is_default", "unit" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "TEMPERATURE", "temperature", "thermometer", true, "°C" },
                    { new Guid("11111111-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "PULSE RATE", "pulse_rate", "heart_pulse", true, "bpm" },
                    { new Guid("11111111-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "RESPIRATORY RATE", "respiratory_rate", "lungs", true, "rpm" },
                    { new Guid("11111111-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "BLOOD PRESSURE", "blood_pressure", "gauge", true, "mmHg" },
                    { new Guid("11111111-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "O2 SATURATION", "o2_saturation", "wind", true, "%" },
                    { new Guid("11111111-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "HEIGHT", "height", "ruler", true, "cm" },
                    { new Guid("11111111-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "WEIGHT", "weight", "weight_scale", true, "kg" },
                    { new Guid("11111111-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Fundal Height", "fundal_height", "ruler", false, "" },
                    { new Guid("11111111-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Fetal Heart Rate", "fetal_heart_rate", "heart_pulse", false, "" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_services_service_id",
                table: "booking_services",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_doctor_id",
                table: "bookings",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_patient_id",
                table: "bookings",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_consultations_booking_id",
                table: "consultations",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_blocked_dates_doctor_id",
                table: "doctor_blocked_dates",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_day_statuses_doctor_id_status_date",
                table: "doctor_day_statuses",
                columns: new[] { "doctor_id", "status_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_schedules_doctor_id_day_of_week",
                table: "doctor_schedules",
                columns: new[] { "doctor_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_services_service_id",
                table: "doctor_services",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_consultation_id",
                table: "follow_ups",
                column: "consultation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medicines_generic_name",
                table: "medicines",
                column: "generic_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_vital_readings_booking_id_template_id",
                table: "patient_vital_readings",
                columns: new[] { "booking_id", "template_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patients_patient_code",
                table: "patients",
                column: "patient_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_booking_id",
                table: "payments",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prescription_line_items_group_id",
                table: "prescription_line_items",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescription_template_items_template_id",
                table: "prescription_template_items",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vital_field_templates_form_key",
                table: "vital_field_templates",
                column: "form_key",
                unique: true);

            // Reporting views (contract §5) — ported from supabase/schema.sql lines 830-886.
            // Translation per DOTNET_BACKEND_PLAN.md §5.6: `||` -> `+`,
            // `count(*) filter (where ...)` -> `SUM(CASE WHEN ... THEN 1 ELSE 0 END)`.
            migrationBuilder.Sql(@"
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
WHERE b.status = 'Completed' AND pay.status = 'Unpaid';");

            migrationBuilder.Sql(@"
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
WHERE f.status = 'Pending';");

            migrationBuilder.Sql(@"
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
GROUP BY b.appointment_date;");

            // Replaces stored rating/reviewCount columns so the average can never drift out of
            // sync with the underlying reviews (see schema.sql comment above the Postgres view).
            migrationBuilder.Sql(@"
CREATE VIEW v_doctor_ratings AS
SELECT
  d.doctor_id,
  COALESCE(ROUND(AVG(CAST(r.rating AS DECIMAL(18,2))), 2), 0) AS average_rating,
  COUNT(r.review_id) AS review_count
FROM doctors d
LEFT JOIN reviews r ON r.doctor_id = d.doctor_id
GROUP BY d.doctor_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_doctor_ratings;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_daily_booking_summary;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_pending_follow_ups;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_unpaid_completed_visits;");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "booking_services");

            migrationBuilder.DropTable(
                name: "clinic_accepted_payment_methods");

            migrationBuilder.DropTable(
                name: "clinic_operating_hours");

            migrationBuilder.DropTable(
                name: "clinic_settings");

            migrationBuilder.DropTable(
                name: "consultation_diagnoses");

            migrationBuilder.DropTable(
                name: "consultations");

            migrationBuilder.DropTable(
                name: "doctor_blocked_dates");

            migrationBuilder.DropTable(
                name: "doctor_day_statuses");

            migrationBuilder.DropTable(
                name: "doctor_favorite_medicines");

            migrationBuilder.DropTable(
                name: "doctor_schedules");

            migrationBuilder.DropTable(
                name: "doctor_services");

            migrationBuilder.DropTable(
                name: "follow_ups");

            migrationBuilder.DropTable(
                name: "icd10codes");

            migrationBuilder.DropTable(
                name: "lab_orders");

            migrationBuilder.DropTable(
                name: "medicines");

            migrationBuilder.DropTable(
                name: "patient_documents");

            migrationBuilder.DropTable(
                name: "patient_lab_results");

            migrationBuilder.DropTable(
                name: "patient_vaccinations");

            migrationBuilder.DropTable(
                name: "patient_vital_readings");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "prescription_line_items");

            migrationBuilder.DropTable(
                name: "prescription_template_items");

            migrationBuilder.DropTable(
                name: "profiles");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "reviews");

            migrationBuilder.DropTable(
                name: "soap_phrases");

            migrationBuilder.DropTable(
                name: "soap_templates");

            migrationBuilder.DropTable(
                name: "vital_field_templates");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "bookings");

            migrationBuilder.DropTable(
                name: "prescription_groups");

            migrationBuilder.DropTable(
                name: "prescription_templates");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "doctors");

            migrationBuilder.DropTable(
                name: "patients");

            migrationBuilder.DropTable(
                name: "staff_accounts");
        }
    }
}
