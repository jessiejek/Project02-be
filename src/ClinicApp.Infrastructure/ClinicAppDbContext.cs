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

    // Clinical
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<ConsultationDiagnosis> ConsultationDiagnoses => Set<ConsultationDiagnosis>();
    public DbSet<VitalFieldTemplate> VitalFieldTemplates => Set<VitalFieldTemplate>();
    public DbSet<PatientVitalReading> PatientVitalReadings => Set<PatientVitalReading>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
    public DbSet<SoapPhrase> SoapPhrases => Set<SoapPhrase>();
    public DbSet<SoapTemplate> SoapTemplates => Set<SoapTemplate>();

    // Prescriptions
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<PrescriptionGroup> PrescriptionGroups => Set<PrescriptionGroup>();
    public DbSet<PrescriptionLineItem> PrescriptionLineItems => Set<PrescriptionLineItem>();
    public DbSet<DoctorFavoriteMedicine> DoctorFavoriteMedicines => Set<DoctorFavoriteMedicine>();
    public DbSet<PrescriptionTemplate> PrescriptionTemplates => Set<PrescriptionTemplate>();
    public DbSet<PrescriptionTemplateItem> PrescriptionTemplateItems => Set<PrescriptionTemplateItem>();

    // Labs & files
    public DbSet<LabOrder> LabOrders => Set<LabOrder>();
    public DbSet<PatientVaccination> PatientVaccinations => Set<PatientVaccination>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<PatientLabResult> PatientLabResults => Set<PatientLabResult>();

    // Admin
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<ClinicSetting> ClinicSettings => Set<ClinicSetting>();
    public DbSet<ClinicOperatingHour> ClinicOperatingHours => Set<ClinicOperatingHour>();
    public DbSet<ClinicAcceptedPaymentMethod> ClinicAcceptedPaymentMethods => Set<ClinicAcceptedPaymentMethod>();

    // Reporting views (keyless)
    public DbSet<VDoctorRating> VDoctorRatings => Set<VDoctorRating>();
    public DbSet<VDailyBookingSummary> VDailyBookingSummaries => Set<VDailyBookingSummary>();
    public DbSet<VUnpaidCompletedVisit> VUnpaidCompletedVisits => Set<VUnpaidCompletedVisit>();
    public DbSet<VPendingFollowUp> VPendingFollowUps => Set<VPendingFollowUp>();

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
        });

        modelBuilder.Entity<StaffAccount>(e =>
        {
            e.HasKey(s => s.StaffId);
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
        });

        modelBuilder.Entity<Review>(e => e.HasKey(r => r.ReviewId));

        // ── Clinical ────────────────────────────────────────────────────
        modelBuilder.Entity<Consultation>(e =>
        {
            e.HasKey(c => c.ConsultationId);
            e.HasIndex(c => c.BookingId).IsUnique(); // upsert conflict key
            e.HasOne(c => c.Booking).WithMany().HasForeignKey(c => c.BookingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConsultationDiagnosis>(e => e.HasKey(d => d.Id));

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
        });

        modelBuilder.Entity<SoapPhrase>(e => e.HasKey(p => p.Id));
        modelBuilder.Entity<SoapTemplate>(e => e.HasKey(t => t.Id));

        // ── Prescriptions ───────────────────────────────────────────────
        modelBuilder.Entity<Medicine>(e =>
        {
            e.HasKey(m => m.MedicineId);
            e.HasIndex(m => m.GenericName).IsUnique();
        });

        modelBuilder.Entity<PrescriptionGroup>(e => e.HasKey(g => g.GroupId));

        modelBuilder.Entity<PrescriptionLineItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasOne(i => i.Group).WithMany(g => g.LineItems).HasForeignKey(i => i.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DoctorFavoriteMedicine>(e => e.HasKey(f => f.Id));

        modelBuilder.Entity<PrescriptionTemplate>(e => e.HasKey(t => t.TemplateId));

        modelBuilder.Entity<PrescriptionTemplateItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasOne(i => i.Template).WithMany(t => t.Items).HasForeignKey(i => i.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Labs & files ────────────────────────────────────────────────
        modelBuilder.Entity<LabOrder>(e => e.HasKey(l => l.LabOrderId));
        modelBuilder.Entity<PatientVaccination>(e => e.HasKey(v => v.Id));
        modelBuilder.Entity<PatientDocument>(e => e.HasKey(d => d.Id));
        modelBuilder.Entity<PatientLabResult>(e => e.HasKey(r => r.Id));

        // ── Admin ───────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLog>(e => e.HasKey(a => a.Id));
        modelBuilder.Entity<Announcement>(e => e.HasKey(a => a.Id));
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

        // ── Reporting views (keyless, mapped to SQL views created in a migration) ──
        modelBuilder.Entity<VDoctorRating>(e => { e.HasNoKey(); e.ToView("v_doctor_ratings"); });
        modelBuilder.Entity<VDailyBookingSummary>(e => { e.HasNoKey(); e.ToView("v_daily_booking_summary"); });
        modelBuilder.Entity<VUnpaidCompletedVisit>(e => { e.HasNoKey(); e.ToView("v_unpaid_completed_visits"); });
        modelBuilder.Entity<VPendingFollowUp>(e => { e.HasNoKey(); e.ToView("v_pending_follow_ups"); });

        SeedData.Apply(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUpdatedAt();
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
