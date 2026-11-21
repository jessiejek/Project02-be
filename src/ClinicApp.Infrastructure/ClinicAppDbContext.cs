using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Infrastructure;

public class ClinicAppDbContext(DbContextOptions<ClinicAppDbContext> options) : DbContext(options)
{
    // Auth
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Identity
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Patient> Patients => Set<Patient>();
    //JJQR - AI (Oct 31, 2026)
    public DbSet<PatientAllergy> PatientAllergies => Set<PatientAllergy>();
    //JJQR - END
    public DbSet<StaffAccount> StaffAccounts => Set<StaffAccount>();
    public DbSet<Doctor> Doctors => Set<Doctor>();

    // Scheduling
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<DoctorBlockedDate> DoctorBlockedDates => Set<DoctorBlockedDate>();
    public DbSet<DoctorDayStatus> DoctorDayStatuses => Set<DoctorDayStatus>();

    // Services
    public DbSet<Service> Services => Set<Service>();
    public DbSet<DoctorService> DoctorServices => Set<DoctorService>();

    // Booking
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingService> BookingServices => Set<BookingService>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<QueueCounter> QueueCounters => Set<QueueCounter>();

    // Clinical
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<MedicalCertificate> MedicalCertificates => Set<MedicalCertificate>();
    public DbSet<ConsultationDiagnosis> ConsultationDiagnoses => Set<ConsultationDiagnosis>();
    public DbSet<VitalFieldTemplate> VitalFieldTemplates => Set<VitalFieldTemplate>();
    public DbSet<PatientVitalReading> PatientVitalReadings => Set<PatientVitalReading>();
    //JJQR - AI (Oct 31, 2026)
    public DbSet<PatientLabValue> PatientLabValues => Set<PatientLabValue>();
    //JJQR - END
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
    public DbSet<SoapPhrase> SoapPhrases => Set<SoapPhrase>();
    public DbSet<SoapTemplate> SoapTemplates => Set<SoapTemplate>();
    public DbSet<MedicalCertificateTemplate> MedicalCertificateTemplates => Set<MedicalCertificateTemplate>();
    public DbSet<DoctorDiagnosisTemplate> DoctorDiagnosisTemplates => Set<DoctorDiagnosisTemplate>();
    //JJQR - AI (Nov 20, 2026) A2
    public DbSet<DoctorBuiltinTemplateCopy> DoctorBuiltinTemplateCopies => Set<DoctorBuiltinTemplateCopy>();
    //JJQR - END

    // Prescriptions
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<PrescriptionGroup> PrescriptionGroups => Set<PrescriptionGroup>();
    public DbSet<PrescriptionLineItem> PrescriptionLineItems => Set<PrescriptionLineItem>();
    public DbSet<DoctorFavoriteMedicine> DoctorFavoriteMedicines => Set<DoctorFavoriteMedicine>();
    public DbSet<PrescriptionTemplate> PrescriptionTemplates => Set<PrescriptionTemplate>();
    public DbSet<PrescriptionTemplateItem> PrescriptionTemplateItems => Set<PrescriptionTemplateItem>();

    // Labs & files
    public DbSet<LabOrder> LabOrders => Set<LabOrder>();
    public DbSet<LabTestCatalog> LabTestCatalog => Set<LabTestCatalog>();
    public DbSet<PatientVaccination> PatientVaccinations => Set<PatientVaccination>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<PatientLabResult> PatientLabResults => Set<PatientLabResult>();

    // Admin
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    // JJQR - AI (Nov 20, 2026) - PV-12 / PV-14
    public DbSet<PatientConsent> PatientConsents => Set<PatientConsent>();
    public DbSet<DataRequest> DataRequests => Set<DataRequest>();
    // JJQR - END
    public DbSet<ClinicSetting> ClinicSettings => Set<ClinicSetting>();
    public DbSet<ClinicOperatingHour> ClinicOperatingHours => Set<ClinicOperatingHour>();
    public DbSet<ClinicAcceptedPaymentMethod> ClinicAcceptedPaymentMethods => Set<ClinicAcceptedPaymentMethod>();

    //JJQR - AI (Nov 21, 2026) cloud sync: node registry, push idempotency log, pull change log
    public DbSet<SyncNode> SyncNodes => Set<SyncNode>();
    public DbSet<SyncOperation> SyncOperations => Set<SyncOperation>();
    public DbSet<SyncChange> SyncChanges => Set<SyncChange>();
    //JJQR - AI (Nov 21, 2026) offline self-edits: per-field last-write times
    public DbSet<SyncFieldStamp> SyncFieldStamps => Set<SyncFieldStamp>();

    /// <summary>Set by the push engine before SaveChanges: the node-clock time to record for a field it applied
    /// (key = aggregate, entity_id, field). Fields not listed are stamped with the cloud clock.</summary>
    public Dictionary<(string Aggregate, string EntityId, string Field), DateTimeOffset> SyncFieldTimes { get; } = new();
    //JJQR - END

    /// <summary>Set by the sync push engine while it applies a node's operation: rows created get
    /// this origin_node_id and the sync_changes rows carry it (pull skips a node's own changes).</summary>
    public Guid? SyncWriterNodeId { get; set; }

    /// <summary>Turn the sync change log off for a context (e.g. a Clinic Node that does not want
    /// cloud-side change rows in its local file). On by default.</summary>
    public bool SyncChangeLogEnabled { get; set; } = true;
    //JJQR - END

    // Reporting views (keyless)
    public DbSet<VDoctorRating> VDoctorRatings => Set<VDoctorRating>();
    public DbSet<VDailyBookingSummary> VDailyBookingSummaries => Set<VDailyBookingSummary>();
    public DbSet<VUnpaidCompletedVisit> VUnpaidCompletedVisits => Set<VUnpaidCompletedVisit>();
    public DbSet<VPendingFollowUp> VPendingFollowUps => Set<VPendingFollowUp>();
    public DbSet<VDoctorEarnings> VDoctorEarnings => Set<VDoctorEarnings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Global: every enum property (nullable or not) serializes/stores as its exact string
        // name (contract §3), never as an int. One line per enum type covers every property
        // that uses it anywhere in the model.
        configurationBuilder.Properties<UserRole>().HaveConversion<string>();
        configurationBuilder.Properties<StaffRole>().HaveConversion<string>();
        configurationBuilder.Properties<StaffStatus>().HaveConversion<string>();
        configurationBuilder.Properties<SexType>().HaveConversion<string>();
        configurationBuilder.Properties<DoctorDayStatusEnum>().HaveConversion<string>();
        configurationBuilder.Properties<ServiceCategory>().HaveConversion<string>();
        configurationBuilder.Properties<BookingStatus>().HaveConversion<string>();
        configurationBuilder.Properties<VisitType>().HaveConversion<string>();
        configurationBuilder.Properties<PaymentMode>().HaveConversion<string>();
        configurationBuilder.Properties<PaymentStatus>().HaveConversion<string>();
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>();
        configurationBuilder.Properties<ProofType>().HaveConversion<string>();
        configurationBuilder.Properties<ConsultationStatus>().HaveConversion<string>();
        configurationBuilder.Properties<DiagnosisType>().HaveConversion<string>();
        configurationBuilder.Properties<SoapField>().HaveConversion<string>();
        configurationBuilder.Properties<LabOrderStatus>().HaveConversion<string>();
        configurationBuilder.Properties<VaccinationStatus>().HaveConversion<string>();
        configurationBuilder.Properties<VaccinationSource>().HaveConversion<string>();
        configurationBuilder.Properties<AuditEntityType>().HaveConversion<string>();
        configurationBuilder.Properties<FollowUpStatus>().HaveConversion<string>();

        // SQLite (the zero-infra dev/demo provider and the integration-test provider) cannot
        // ORDER BY a DateTimeOffset column. Store them as sortable binary there; SQL Server keeps
        // its native datetimeoffset. No effect on the production provider.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            configurationBuilder.Properties<DateTimeOffset>()
                .HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
        }

        // Sensible default column type for money columns; override per-property below only
        // where the contract needs something different (none currently do).
        configurationBuilder.Properties<decimal>().HaveColumnType("decimal(18,2)");
        configurationBuilder.Properties<decimal?>().HaveColumnType("decimal(18,2)");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Auth ────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Identity ────────────────────────────────────────────────────
        modelBuilder.Entity<Profile>(e =>
        {
            e.HasKey(p => p.Id);
        });

        modelBuilder.Entity<Patient>(e =>
        {
            e.HasKey(p => p.PatientId);
            e.HasIndex(p => p.PatientCode).IsUnique();
            // schema.sql: patients.user_id uuid unique — nullable, so filtered.
            e.HasIndex(p => p.UserId).IsUnique().HasFilter("[user_id] IS NOT NULL");
        });

        //JJQR - AI (Oct 31, 2026)
        modelBuilder.Entity<PatientAllergy>(e =>
        {
            e.ToTable("patient_allergies");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.PatientId);
            e.HasOne(a => a.Patient).WithMany(p => p.Allergies).HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Cascade);
        });
        //JJQR - END

        modelBuilder.Entity<StaffAccount>(e =>
        {
            e.HasKey(s => s.StaffId);
            // schema.sql: staff_accounts.user_id uuid not null unique.
            e.HasIndex(s => s.UserId).IsUnique();
        });

        modelBuilder.Entity<Doctor>(e =>
        {
            // Shared PK / 1:1 with StaffAccount: DoctorId = StaffAccounts.StaffId.
            e.HasKey(d => d.DoctorId);
            e.HasOne(d => d.StaffAccount)
                .WithOne(s => s.Doctor)
                .HasForeignKey<Doctor>(d => d.DoctorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Scheduling ──────────────────────────────────────────────────
        modelBuilder.Entity<DoctorSchedule>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => new { s.DoctorId, s.DayOfWeek }).IsUnique(); // conflict key
            e.HasOne(s => s.Doctor).WithMany().HasForeignKey(s => s.DoctorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoctorBlockedDate>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasOne(b => b.Doctor).WithMany().HasForeignKey(b => b.DoctorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoctorDayStatus>(e =>
        {
            e.HasKey(d => d.Id);
            e.HasIndex(d => new { d.DoctorId, d.StatusDate }).IsUnique(); // conflict key
            e.HasOne(d => d.Doctor).WithMany().HasForeignKey(d => d.DoctorId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Services ────────────────────────────────────────────────────
        modelBuilder.Entity<Service>(e => e.HasKey(s => s.ServiceId));

        modelBuilder.Entity<DoctorService>(e =>
        {
            e.HasKey(ds => new { ds.DoctorId, ds.ServiceId }); // composite PK
            e.HasOne(ds => ds.Doctor).WithMany().HasForeignKey(ds => ds.DoctorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ds => ds.Service).WithMany().HasForeignKey(ds => ds.ServiceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Booking ─────────────────────────────────────────────────────
        modelBuilder.Entity<Booking>(e =>
        {
            e.HasKey(b => b.BookingId);
            e.HasOne(b => b.Patient).WithMany().HasForeignKey(b => b.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Doctor).WithMany().HasForeignKey(b => b.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BookingService>(e =>
        {
            e.HasKey(bs => new { bs.BookingId, bs.ServiceId }); // composite PK
            e.HasOne(bs => bs.Booking).WithMany(b => b.BookingServices).HasForeignKey(bs => bs.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(bs => bs.Service).WithMany().HasForeignKey(bs => bs.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(p => p.PaymentId);
            e.HasIndex(p => p.BookingId).IsUnique(); // 1:1 with Booking
            e.HasOne(p => p.Booking).WithOne(b => b.Payment).HasForeignKey<Payment>(p => p.BookingId).OnDelete(DeleteBehavior.Cascade);
            //JJQR - AI (21 Nov 2026) OR numbers: max 100 chars, and unique among ONLINE (cloud-written, origin_node_id IS NULL) payments only. Payments accepted from a clinic node keep the OR as printed even if it clashes (flagged, never dropped), so they are outside the index.
            // Uniqueness is case-insensitive because it follows the column / database collation (SQL Server default SQL_Latin1_General_CP1_CI_AS; the app also checks case-insensitively in code).
            e.Property(p => p.OrNumber).HasMaxLength(100);
            e.HasIndex(p => p.OrNumber).IsUnique().HasDatabaseName("ix_payments_or_number_cloud").HasFilter("[or_number] IS NOT NULL AND [origin_node_id] IS NULL");
            //JJQR - END
        });

        modelBuilder.Entity<Review>(e => e.HasKey(r => r.ReviewId));
        //JJQR - AI (Nov 21, 2026) - priority lane: one counter row per (day, lane) so R and S each count from 1
        modelBuilder.Entity<QueueCounter>(e =>
        {
            e.HasKey(c => new { c.Date, c.Lane });
            e.Property(c => c.Lane).HasMaxLength(1);
        });
        //JJQR - END

        // ── Clinical ────────────────────────────────────────────────────
        modelBuilder.Entity<Consultation>(e =>
        {
            e.HasKey(c => c.ConsultationId);
            e.HasIndex(c => c.BookingId).IsUnique(); // upsert conflict key
            e.HasOne(c => c.Booking).WithMany().HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Doctor).WithMany().HasForeignKey(c => c.DoctorId).OnDelete(DeleteBehavior.NoAction);
            e.HasMany(c => c.ConsultationDiagnoses).WithOne().HasForeignKey(d => d.ConsultationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.FollowUp).WithOne().HasForeignKey<FollowUp>(f => f.ConsultationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConsultationDiagnosis>(e => e.HasKey(d => d.Id));

        modelBuilder.Entity<MedicalCertificate>(e =>
        {
            e.HasKey(m => m.CertificateId);
            e.HasIndex(m => m.ConsultationId).IsUnique(); // upsert conflict key
            e.HasOne<Consultation>().WithMany().HasForeignKey(m => m.ConsultationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Patient>().WithMany().HasForeignKey(m => m.PatientId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Doctor>().WithMany().HasForeignKey(m => m.DoctorId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<VitalFieldTemplate>(e =>
        {
            e.HasKey(t => t.TemplateId);
            e.HasIndex(t => t.FormKey).IsUnique();
        });

        modelBuilder.Entity<PatientVitalReading>(e =>
        {
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.BookingId, r.TemplateId }).IsUnique(); // conflict key
        });

        //JJQR - AI (Oct 31, 2026)
        modelBuilder.Entity<PatientLabValue>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasIndex(v => v.PatientId);
            e.HasOne<Patient>().WithMany().HasForeignKey(v => v.PatientId).OnDelete(DeleteBehavior.Cascade);
        });
        //JJQR - END

        modelBuilder.Entity<FollowUp>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasIndex(f => f.ConsultationId).IsUnique(); // conflict key
        });

        // Explicit table name: the snake_case convention would map Icd10Code -> "icd10codes",
        // but contract §4 / schema.sql call this table "icd10_codes".
        modelBuilder.Entity<Icd10Code>(e =>
        {
            e.ToTable("icd10_codes");
            e.HasKey(i => i.Code);
            //JJQR - AI (Nov 20, 2026) B1/B3
            e.Property(i => i.Level).HasDefaultValue(0);
            e.Property(i => i.IsLeaf).HasDefaultValue(true);
            e.Property(i => i.Description).HasMaxLength(450);
            e.HasIndex(i => i.Description);
            e.HasIndex(i => i.IsLeaf);
            //JJQR - END
        });

        modelBuilder.Entity<SoapPhrase>(e =>
        {
            e.HasKey(p => p.Id);
            //JJQR - AI (Nov 20, 2026) A2
            e.Property(p => p.SourceBuiltinKey).HasMaxLength(64);
            //JJQR - END
        });
        modelBuilder.Entity<SoapTemplate>(e =>
        {
            e.HasKey(t => t.Id);
            //JJQR - AI (Nov 20, 2026) A2
            e.Property(t => t.SourceBuiltinKey).HasMaxLength(64);
            //JJQR - END
        });
        modelBuilder.Entity<MedicalCertificateTemplate>(e =>
        {
            e.HasKey(t => t.Id);
            //JJQR - AI (Nov 20, 2026) A2
            e.Property(t => t.SourceBuiltinKey).HasMaxLength(64);
            //JJQR - END
        });
        modelBuilder.Entity<DoctorDiagnosisTemplate>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.DoctorId);
            //JJQR - AI (Nov 20, 2026) A2
            e.Property(t => t.SourceBuiltinKey).HasMaxLength(64);
            //JJQR - END
        });
        //JJQR - AI (Nov 20, 2026) A2
        modelBuilder.Entity<DoctorBuiltinTemplateCopy>(e =>
        {
            e.ToTable("doctor_builtin_template_copies");
            e.HasKey(c => c.Id);
            e.Property(c => c.Kind).HasMaxLength(20);
            e.Property(c => c.BuiltinKey).HasMaxLength(64);
            e.HasIndex(c => new { c.DoctorId, c.Kind, c.BuiltinKey }).IsUnique();
        });
        //JJQR - END

        // ── Prescriptions ───────────────────────────────────────────────
        modelBuilder.Entity<Medicine>(e =>
        {
            e.HasKey(m => m.MedicineId);
            e.HasIndex(m => m.GenericName).IsUnique();
        });

        modelBuilder.Entity<PrescriptionGroup>(e =>
        {
            e.HasKey(g => g.GroupId);
            e.HasOne(g => g.Booking).WithMany().HasForeignKey(g => g.BookingId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientDocument>(e =>
            e.HasOne(d => d.Booking).WithMany().HasForeignKey(d => d.BookingId).OnDelete(DeleteBehavior.NoAction));
        modelBuilder.Entity<PatientLabResult>(e =>
            e.HasOne(r => r.Booking).WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.NoAction));

        modelBuilder.Entity<PrescriptionLineItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasOne(i => i.Group).WithMany(g => g.LineItems).HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoctorFavoriteMedicine>(e => e.HasKey(f => f.Id));

        modelBuilder.Entity<PrescriptionTemplate>(e =>
        {
            e.HasKey(t => t.TemplateId);
            //JJQR - AI (Nov 20, 2026) A2
            e.Property(t => t.SourceBuiltinKey).HasMaxLength(64);
            //JJQR - END
        });

        modelBuilder.Entity<PrescriptionTemplateItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasOne(i => i.Template).WithMany(t => t.Items).HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Labs & files ────────────────────────────────────────────────
        modelBuilder.Entity<LabOrder>(e =>
        {
            e.HasKey(l => l.LabOrderId);
            e.HasOne<LabTestCatalog>().WithMany().HasForeignKey(l => l.LabTestId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LabTestCatalog>(e =>
        {
            e.HasKey(t => t.LabTestId);
            e.HasIndex(t => t.Name).IsUnique();
        });
        modelBuilder.Entity<PatientVaccination>(e => e.HasKey(v => v.Id));
        modelBuilder.Entity<PatientDocument>(e => e.HasKey(d => d.Id));
        modelBuilder.Entity<PatientLabResult>(e => e.HasKey(r => r.Id));

        // ── Admin ───────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e => e.HasKey(a => a.Id));
        modelBuilder.Entity<Announcement>(e => e.HasKey(a => a.Id));
        // JJQR - AI (Nov 20, 2026) - PV-12 / PV-14
        modelBuilder.Entity<PatientConsent>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Decision).HasMaxLength(20);
            e.HasIndex(c => new { c.PatientId, c.DecidedAt });
        });
        modelBuilder.Entity<DataRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Kind).HasMaxLength(20);
            e.Property(r => r.Status).HasMaxLength(20);
            e.Property(r => r.Details).HasMaxLength(1000);
            e.Property(r => r.ResolutionNote).HasMaxLength(1000);
            e.HasIndex(r => new { r.PatientId, r.RequestedAt });
        });
        // JJQR - END
        modelBuilder.Entity<ClinicSetting>(e => e.HasKey(c => c.Id));
        modelBuilder.Entity<ClinicOperatingHour>(e =>
        {
            e.HasKey(h => h.DayOfWeek);
            // DayOfWeek=0 (Sunday) is a legitimate seeded PK value, but EF's "was this key provided?"
            // heuristic for HasData treats default(short) as "omitted" unless value generation is
            // explicitly turned off for this manually-assigned key.
            e.Property(h => h.DayOfWeek).ValueGeneratedNever();
        });
        modelBuilder.Entity<ClinicAcceptedPaymentMethod>(e => e.HasKey(p => p.PaymentMethod));

        //JJQR - AI (Nov 21, 2026) cloud sync tables
        modelBuilder.Entity<SyncNode>(e =>
        {
            e.ToTable("sync_nodes");
            e.HasKey(n => n.NodeId);
            e.Property(n => n.NodeId).ValueGeneratedNever();
            e.Property(n => n.Name).HasMaxLength(200);
            e.Property(n => n.Platform).HasMaxLength(50);
            e.Property(n => n.AppVersion).HasMaxLength(50);
            e.Property(n => n.SecretHash).HasMaxLength(128);
            e.Property(n => n.Status).HasMaxLength(20);
        });
        modelBuilder.Entity<SyncOperation>(e =>
        {
            e.ToTable("sync_operations");
            e.HasKey(o => o.OperationId);
            e.Property(o => o.OperationId).ValueGeneratedNever();
            e.Property(o => o.Aggregate).HasMaxLength(64);
            e.Property(o => o.EntityId).HasMaxLength(100);
            e.Property(o => o.OperationType).HasMaxLength(32);
            e.Property(o => o.Status).HasMaxLength(20);
            e.Property(o => o.ReasonCode).HasMaxLength(64);
            e.HasIndex(o => new { o.NodeId, o.ReceivedAt });
        });
        modelBuilder.Entity<SyncChange>(e =>
        {
            e.ToTable("sync_changes");
            e.HasKey(c => c.Seq);
            e.Property(c => c.Seq).ValueGeneratedOnAdd();
            e.Property(c => c.Aggregate).HasMaxLength(64);
            e.Property(c => c.EntityId).HasMaxLength(100);
            e.HasIndex(c => new { c.Aggregate, c.EntityId });
        });
        //JJQR - AI (Nov 21, 2026) offline self-edits
        modelBuilder.Entity<SyncFieldStamp>(e =>
        {
            e.ToTable("sync_field_stamps");
            e.HasKey(s => new { s.Aggregate, s.EntityId, s.Field });
            e.Property(s => s.Aggregate).HasMaxLength(64);
            e.Property(s => s.EntityId).HasMaxLength(100);
            e.Property(s => s.Field).HasMaxLength(64);
        });
        //JJQR - END
        //JJQR - END

        // ── Reporting views (keyless, mapped to SQL views created in a migration) ──
        modelBuilder.Entity<VDoctorRating>(e => { e.HasNoKey(); e.ToView("v_doctor_ratings"); });
        modelBuilder.Entity<VDailyBookingSummary>(e => { e.HasNoKey(); e.ToView("v_daily_booking_summary"); });
        modelBuilder.Entity<VUnpaidCompletedVisit>(e => { e.HasNoKey(); e.ToView("v_unpaid_completed_visits"); });
        modelBuilder.Entity<VPendingFollowUp>(e => { e.HasNoKey(); e.ToView("v_pending_follow_ups"); });
        modelBuilder.Entity<VDoctorEarnings>(e => { e.HasNoKey(); e.ToView("v_doctor_earnings"); });

        SeedData.Apply(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUpdatedAt();
        //JJQR - AI (Nov 21, 2026) cloud sync change log
        if (SyncChangeLogEnabled) ClinicApp.Infrastructure.Sync.SyncChangeRecorder.Record(this);
        //JJQR - END
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUpdatedAt();
        //JJQR - AI (Nov 21, 2026) cloud sync change log
        if (SyncChangeLogEnabled) ClinicApp.Infrastructure.Sync.SyncChangeRecorder.Record(this);
        //JJQR - END
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Replaces Postgres's set_updated_at() trigger: stamp UpdatedAt on every modified
    /// entity that implements IHasUpdatedAt.</summary>
    private void StampUpdatedAt()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IHasUpdatedAt>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
