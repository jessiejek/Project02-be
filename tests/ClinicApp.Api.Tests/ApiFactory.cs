using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicApp.Api.Tests;

/// <summary>Boots the real API in-process against a throwaway SQLite database and a throwaway
/// upload folder, seeded with two of everything (see <see cref="World"/>). No mocking of authn or
/// authz: requests carry real signed JWTs, so these tests exercise the same pipeline production does.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "clinicapp-tests-" + Guid.NewGuid().ToString("N"));
    public string UploadsRoot => Path.Combine(Root, "uploads");
    public World World { get; private set; } = null!;

    public ApiFactory()
    {
        Directory.CreateDirectory(UploadsRoot);

        // Program.cs reads config eagerly while building, so env vars (not
        // ConfigureAppConfiguration) are what reliably reach it.
        Environment.SetEnvironmentVariable("Database__Provider", "Sqlite");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", $"Data Source={Path.Combine(Root, "test.db")}");
        Environment.SetEnvironmentVariable("Jwt__Secret", "integration-test-secret-0123456789-abcdefghijklmnopqrstuvwxyz");
        Environment.SetEnvironmentVariable("FileStorage__RootPath", UploadsRoot);
        Environment.SetEnvironmentVariable("RateLimiting__GlobalPermitPerMinute", "1000000");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitPerMinute", "1000000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

    /// <summary>Seeds once, after the host (and SQLite schema) exist.</summary>
    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        World = await World.SeedAsync(scope.ServiceProvider.GetRequiredService<ClinicAppDbContext>(), UploadsRoot);
    }

    public HttpClient ClientFor(Guid? userId, string? role)
    {
        var client = CreateClient();
        if (userId is not null && role is not null)
        {
            var token = Services.GetRequiredService<ITokenService>().CreateAccessToken(userId.Value, role);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    public HttpClient Anonymous() => ClientFor(null, null);
    public HttpClient Patient(Person p) => ClientFor(p.UserId, "Patient");
    public HttpClient As(Person p, string role) => ClientFor(p.UserId, role);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static StringContent Body(object payload) =>
        new(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
}

/// <summary>A seeded login: their user id, their patient / staff row id, and (for patients) a booking.</summary>
public sealed record Person(Guid UserId, Guid RowId);

/// <summary>Everything the tests poke at. "A" and "B" are two unrelated patients; DoctorA owns every
/// seeded booking, DoctorB is a second doctor who must not be able to touch DoctorA's records.</summary>
public sealed class World
{
    public Person Admin { get; init; } = null!;
    public Person Staff { get; init; } = null!;
    public Person DoctorA { get; init; } = null!;
    public Person DoctorB { get; init; } = null!;
    public Person PatientA { get; init; } = null!;
    public Person PatientB { get; init; } = null!;

    public Guid BookingA { get; init; }
    public Guid BookingB { get; init; }
    public Guid PaymentA { get; init; }
    public Guid PaymentB { get; init; }
    public Guid ConsultationA { get; init; }
    public Guid ConsultationB { get; init; }
    public Guid RxGroupA { get; init; }
    public Guid RxGroupB { get; init; }
    public Guid ReviewB { get; init; }
    public Guid DocumentA { get; init; }
    public Guid DocumentB { get; init; }
    public Guid LabResultA { get; init; }
    public Guid LabResultB { get; init; }
    public Guid FavoriteDoctorA { get; init; }
    public Guid SoapTemplateDoctorA { get; init; }

    public const string DocumentBContent = "PATIENT-B-SECRET-DOCUMENT";
    public const string DocumentAContent = "PATIENT-A-OWN-DOCUMENT";

    public static async Task<World> SeedAsync(ClinicAppDbContext db, string uploadsRoot)
    {
        var now = DateTimeOffset.UtcNow;
        var today = ClinicApp.Domain.ClinicClock.Today;

        Person NewLogin(UserRole role)
        {
            var id = Guid.NewGuid();
            db.Users.Add(new User { Id = id, Email = $"{role}-{id:N}@test.local", PasswordHash = "x", EmailConfirmed = true, CreatedAt = now });
            db.Profiles.Add(new Profile { Id = id, Role = role, CreatedAt = now });
            return new Person(id, Guid.NewGuid());
        }

        Person NewStaff(UserRole role, StaffRole staffRole)
        {
            var p = NewLogin(role);
            db.StaffAccounts.Add(new StaffAccount
            {
                StaffId = p.RowId, UserId = p.UserId, FullName = $"{role} {p.RowId.ToString()[..4]}",
                Email = $"{role}-{p.RowId:N}@test.local", Role = staffRole, Status = StaffStatus.Active,
                InvitedAt = now, CreatedAt = now, UpdatedAt = now
            });
            return p;
        }

        Person NewPatient(string first)
        {
            var p = NewLogin(UserRole.Patient);
            db.Patients.Add(new Patient
            {
                PatientId = p.RowId, UserId = p.UserId, PatientCode = $"MF-{first}", FirstName = first, LastName = "Tester",
                DateOfBirth = new DateOnly(1990, 1, 1), Sex = SexType.Female, Email = $"{first}@test.local",
                ContactNumber = "0917" + first, CreatedAt = now, UpdatedAt = now
            });
            return p;
        }

        var admin = NewStaff(UserRole.Admin, StaffRole.Admin);
        var staff = NewStaff(UserRole.Staff, StaffRole.Staff);
        var docA = NewStaff(UserRole.Doctor, StaffRole.Doctor);
        var docB = NewStaff(UserRole.Doctor, StaffRole.Doctor);
        foreach (var d in new[] { docA, docB })
            db.Doctors.Add(new Doctor { DoctorId = d.RowId, Specialization = "GP", ConsultationFee = 450, SlotDurationMinutes = 15, SlotCapacity = 1, CreatedAt = now, UpdatedAt = now });
        var patA = NewPatient("Alice");
        var patB = NewPatient("Bob");
        await db.SaveChangesAsync();

        Guid NewBooking(Person patient) => AddBooking(db, patient.RowId, docA.RowId, today, now);
        var bookingA = NewBooking(patA);
        var bookingB = NewBooking(patB);
        await db.SaveChangesAsync();

        Guid PaymentOf(Guid booking) => db.Payments.Local.Single(p => p.BookingId == booking).PaymentId;
        Guid NewConsult(Guid booking, Person patient)
        {
            var id = Guid.NewGuid();
            db.Consultations.Add(new Consultation
            {
                ConsultationId = id, BookingId = booking, PatientId = patient.RowId, DoctorId = docA.RowId,
                Status = ConsultationStatus.Draft, ChiefComplaint = "cough", DoctorNotes = $"private note for {patient.RowId}",
                CreatedAt = now, UpdatedAt = now
            });
            return id;
        }
        var consultA = NewConsult(bookingA, patA);
        var consultB = NewConsult(bookingB, patB);
        await db.SaveChangesAsync();

        Guid NewRx(Guid booking, Person patient)
        {
            var id = Guid.NewGuid();
            db.PrescriptionGroups.Add(new PrescriptionGroup { GroupId = id, BookingId = booking, PatientId = patient.RowId, DoctorId = docA.RowId, CreatedAt = now, UpdatedAt = now });
            return id;
        }
        var rxA = NewRx(bookingA, patA);
        var rxB = NewRx(bookingB, patB);

        foreach (var (c, p) in new[] { (consultA, patA), (consultB, patB) })
        {
            db.LabOrders.Add(new LabOrder { LabOrderId = Guid.NewGuid(), ConsultationId = c, PatientId = p.RowId, DoctorId = docA.RowId, TestName = "CBC", Status = LabOrderStatus.Requested, RequestedAt = now, CreatedAt = now, UpdatedAt = now });
            db.FollowUps.Add(new FollowUp { Id = Guid.NewGuid(), ConsultationId = c, PatientId = p.RowId, DoctorId = docA.RowId, FollowUpDate = today.AddDays(7), Status = FollowUpStatus.Pending, CreatedAt = now, UpdatedAt = now });
            db.MedicalCertificates.Add(new MedicalCertificate { CertificateId = Guid.NewGuid(), ConsultationId = c, PatientId = p.RowId, DoctorId = docA.RowId, IssueDate = today, DiagnosisText = "secret dx", CreatedAt = now, UpdatedAt = now });
            db.PatientVaccinations.Add(new PatientVaccination { Id = Guid.NewGuid(), PatientId = p.RowId, ConsultationId = c, VaccineName = "Flu", Status = VaccinationStatus.Administered, Source = VaccinationSource.AdministeredInClinic, AdministeredDate = today, CreatedAt = now, UpdatedAt = now });
            db.ConsultationDiagnoses.Add(new ConsultationDiagnosis { Id = Guid.NewGuid(), ConsultationId = c, CustomDescription = "secret diagnosis", Type = DiagnosisType.Primary, CreatedAt = now });
        }

        var reviewB = Guid.NewGuid();
        db.Reviews.Add(new Review { ReviewId = reviewB, BookingId = bookingB, DoctorId = docA.RowId, PatientId = patB.RowId, Rating = 5, Comment = "bob's review", CreatedAt = now });

        // Real files on disk, laid out exactly as LocalFileStorageService writes them.
        string WriteFile(Person patient, Guid booking, string content)
        {
            var dir = Path.Combine(uploadsRoot, patient.RowId.ToString(), booking.ToString());
            Directory.CreateDirectory(dir);
            var name = $"{Guid.NewGuid():N}.pdf";
            File.WriteAllText(Path.Combine(dir, name), content);
            return $"/uploads/{patient.RowId}/{booking}/{name}";
        }
        var docAId = Guid.NewGuid();
        var docBId = Guid.NewGuid();
        var labAId = Guid.NewGuid();
        var labBId = Guid.NewGuid();
        db.PatientDocuments.Add(new PatientDocument { Id = docAId, PatientId = patA.RowId, BookingId = bookingA, FileName = "a.pdf", FileContentType = "application/pdf", FileSize = 1, FileUrl = WriteFile(patA, bookingA, World.DocumentAContent), UploadedAt = now });
        db.PatientDocuments.Add(new PatientDocument { Id = docBId, PatientId = patB.RowId, BookingId = bookingB, FileName = "b.pdf", FileContentType = "application/pdf", FileSize = 1, FileUrl = WriteFile(patB, bookingB, World.DocumentBContent), UploadedAt = now });
        db.PatientLabResults.Add(new PatientLabResult { Id = labAId, PatientId = patA.RowId, BookingId = bookingA, FileName = "a-lab.pdf", FileContentType = "application/pdf", FileUrl = WriteFile(patA, bookingA, "A-LAB"), UploadedAt = now });
        db.PatientLabResults.Add(new PatientLabResult { Id = labBId, PatientId = patB.RowId, BookingId = bookingB, FileName = "b-lab.pdf", FileContentType = "application/pdf", FileUrl = WriteFile(patB, bookingB, "B-LAB"), UploadedAt = now });

        var favorite = Guid.NewGuid();
        var medicineId = Guid.NewGuid();
        db.Medicines.Add(new Medicine { MedicineId = medicineId, GenericName = "Paracetamol", CreatedAt = now });
        db.DoctorFavoriteMedicines.Add(new DoctorFavoriteMedicine { Id = favorite, DoctorId = docA.RowId, MedicineId = medicineId, GenericName = "Paracetamol", Dosage = "500mg", Quantity = "10", CreatedAt = now });
        var soapTemplate = Guid.NewGuid();
        db.SoapTemplates.Add(new SoapTemplate { Id = soapTemplate, DoctorId = docA.RowId, Title = "DoctorA private", CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync();

        return new World
        {
            Admin = admin, Staff = staff, DoctorA = docA, DoctorB = docB, PatientA = patA, PatientB = patB,
            BookingA = bookingA, BookingB = bookingB, PaymentA = PaymentOf(bookingA), PaymentB = PaymentOf(bookingB),
            ConsultationA = consultA, ConsultationB = consultB, RxGroupA = rxA, RxGroupB = rxB, ReviewB = reviewB,
            DocumentA = docAId, DocumentB = docBId, LabResultA = labAId, LabResultB = labBId,
            FavoriteDoctorA = favorite, SoapTemplateDoctorA = soapTemplate
        };
    }

    /// <summary>A fresh Completed booking + Unpaid payment for tests that mutate money state.</summary>
    public static async Task<(Guid BookingId, Guid PaymentId)> NewBookingAsync(ClinicAppDbContext db, Guid patientId, Guid doctorId)
    {
        var now = DateTimeOffset.UtcNow;
        var bookingId = AddBooking(db, patientId, doctorId, ClinicApp.Domain.ClinicClock.Today, now);
        await db.SaveChangesAsync();
        return (bookingId, db.Payments.Local.Single(p => p.BookingId == bookingId).PaymentId);
    }

    private static Guid AddBooking(ClinicAppDbContext db, Guid patientId, Guid doctorId, DateOnly date, DateTimeOffset now)
    {
        var id = Guid.NewGuid();
        db.Bookings.Add(new Booking
        {
            BookingId = id, PatientId = patientId, DoctorId = doctorId, AppointmentDate = date,
            Status = BookingStatus.Completed, PaymentMode = PaymentMode.PayAtClinic, QueueNumber = "Q-" + id.ToString()[..4],
            ConsultationFeeSnapshot = 450, TotalFee = 450, AmountDue = 450, CreatedAt = now, UpdatedAt = now
        });
        db.Payments.Add(new Payment { PaymentId = Guid.NewGuid(), BookingId = id, Amount = 450, Status = PaymentStatus.Unpaid, CreatedAt = now, UpdatedAt = now });
        return id;
    }
}
