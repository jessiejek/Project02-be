using System.Net;
using System.Text.Json;
using ClinicApp.Auth;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 19, 2026) - Phase 5 (items 1-6): a Doctor is forced to their OWN doctor id on the 3 report endpoints and the GET lists of bookings, consultations, prescription-groups, lab-orders, medical-certificates, follow-ups. Staff / Admin unchanged.
[Collection("api")]
public class DoctorScopeTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");   // owns the seeded clinical rows
    // Other test classes also write rows for the seeded doctor B, so "a doctor with nothing" is always a FRESH doctor.
    private async Task<(Guid UserId, Guid DoctorId)> NewEmptyDoctor() => await WithDb(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        var uid = Guid.NewGuid(); var sid = Guid.NewGuid();
        db.Users.Add(new User { Id = uid, Email = $"scope-{uid:N}@test.local", PasswordHash = "x", EmailConfirmed = true, CreatedAt = now });
        db.Profiles.Add(new Profile { Id = uid, Role = UserRole.Doctor, CreatedAt = now });
        db.StaffAccounts.Add(new StaffAccount { StaffId = sid, UserId = uid, FullName = "Scope Doctor", Email = $"scope-{sid:N}@test.local", Role = StaffRole.Doctor, Status = StaffStatus.Active, InvitedAt = now, CreatedAt = now, UpdatedAt = now });
        db.Doctors.Add(new Doctor { DoctorId = sid, Specialization = "GP", ConsultationFee = 450, SlotDurationMinutes = 15, SlotCapacity = 1, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return (uid, sid);
    });
    private HttpClient StaffC() => F.As(W.Staff, "Staff");
    private HttpClient AdminC() => F.As(W.Admin, "Admin");

    public static IEnumerable<object[]> Lists() =>
    [
        ["/api/bookings"], ["/api/consultations"], ["/api/prescription-groups"],
        ["/api/lab-orders"], ["/api/medical-certificates"], ["/api/follow-ups"],
    ];

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Doctor_B_sees_none_of_doctor_As_rows_even_when_asking_for_them(string path)
    {
        var (uid, _) = await NewEmptyDoctor();
        using var b = F.ClientFor(uid, "Doctor");
        Assert.Empty(await ListOf(await b.GetAsync(path)));
        // Spoofing: asking for A's id is replaced by the caller's own id (lab-orders has no doctorId param — the doctor filter still applies).
        Assert.Empty(await ListOf(await b.GetAsync($"{path}?doctorId={W.DoctorA.RowId}")));
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Doctor_A_still_sees_their_own_rows_and_a_spoofed_other_doctor_id_changes_nothing(string path)
    {
        using var a = DocA();
        var own = await ListOf(await a.GetAsync(path));
        Assert.NotEmpty(own);
        Assert.All(own, r => Assert.Equal(W.DoctorA.RowId, r.GetProperty("doctor_id").GetGuid())); // never anyone else's row
        // A asks for B's rows: forced back to A's own id → same rows, never B's.
        var spoof = await ListOf(await a.GetAsync($"{path}?doctorId={W.DoctorB.RowId}"));
        Assert.Equal(own.Count, spoof.Count);
        Assert.All(spoof, r => Assert.Equal(W.DoctorA.RowId, r.GetProperty("doctor_id").GetGuid()));
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task Staff_and_admin_behaviour_is_unchanged(string path)
    {
        using var staff = StaffC();
        using var admin = AdminC();
        var staffAll = await ListOf(await staff.GetAsync(path));
        Assert.NotEmpty(staffAll);                       // staff still see every doctor's rows
        var adminAll = await ListOf(await admin.GetAsync(path));
        Assert.Equal(staffAll.Count, adminAll.Count);
        if (path is not "/api/lab-orders")               // lab-orders has no doctorId filter
        {
            var (_, emptyDoctor) = await NewEmptyDoctor();
            Assert.Empty(await ListOf(await admin.GetAsync($"{path}?doctorId={emptyDoctor}"))); // admin's explicit filter still works
            var onlyA = await ListOf(await admin.GetAsync($"{path}?doctorId={W.DoctorA.RowId}"));
            Assert.NotEmpty(onlyA);
            Assert.All(onlyA, r => Assert.Equal(W.DoctorA.RowId, r.GetProperty("doctor_id").GetGuid()));
        }
    }

    [Fact]
    public async Task Patient_rules_are_unchanged()
    {
        using var p = F.Patient(W.PatientA);
        Assert.All(await ListOf(await p.GetAsync("/api/bookings")), r => Assert.Equal(W.PatientA.RowId, r.GetProperty("patient_id").GetGuid()));
        await ShouldBe(HttpStatusCode.Forbidden, await p.GetAsync($"/api/bookings?patientId={W.PatientB.RowId}"));
    }

    // ── reports ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Unpaid_and_follow_up_reports_are_scoped_to_the_doctor()
    {
        using var a = DocA();
        var (uid, _) = await NewEmptyDoctor();
        using var b = F.ClientFor(uid, "Doctor");
        using var staff = StaffC();
        Assert.Empty(await ListOf(await b.GetAsync("/api/reports/unpaid-completed-visits")));
        Assert.Empty(await ListOf(await b.GetAsync("/api/reports/pending-follow-ups")));

        var aUnpaid = await ListOf(await a.GetAsync("/api/reports/unpaid-completed-visits"));
        var aFollow = await ListOf(await a.GetAsync("/api/reports/pending-follow-ups"));
        Assert.NotEmpty(aUnpaid);
        Assert.NotEmpty(aFollow);
        Assert.All(aUnpaid, r => Assert.Equal(W.DoctorA.RowId, r.GetProperty("doctor_id").GetGuid()));
        Assert.All(aFollow, r => Assert.Equal(W.DoctorA.RowId, r.GetProperty("doctor_id").GetGuid()));

        // staff keep the clinic-wide views
        Assert.True((await ListOf(await staff.GetAsync("/api/reports/unpaid-completed-visits"))).Count >= aUnpaid.Count);
        Assert.True((await ListOf(await staff.GetAsync("/api/reports/pending-follow-ups"))).Count >= aFollow.Count);
    }

    [Fact]
    public async Task Daily_summary_for_a_doctor_is_computed_from_only_their_bookings()
    {
        // A fresh doctor with a known set of bookings; another doctor's money on the same day must not leak in.
        var today = ClinicClock.Today;
        var (userId, sid) = await NewEmptyDoctor();
        await WithDb<int>(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            void Add(BookingStatus st, PaymentStatus pay, decimal fee, decimal? received)
            {
                var id = Guid.NewGuid();
                db.Bookings.Add(new Booking { BookingId = id, PatientId = W.PatientA.RowId, DoctorId = sid, AppointmentDate = today, Status = st, PaymentMode = PaymentMode.PayAtClinic, QueueNumber = "S-" + id.ToString("N")[..8], ConsultationFeeSnapshot = fee, TotalFee = fee, AmountDue = 0, CreatedAt = now, UpdatedAt = now });
                db.Payments.Add(new Payment { PaymentId = Guid.NewGuid(), BookingId = id, Amount = fee, Status = pay, AmountReceived = received, CreatedAt = now, UpdatedAt = now });
            }
            Add(BookingStatus.Completed, PaymentStatus.Paid, 500, 500);
            Add(BookingStatus.Completed, PaymentStatus.Paid, 300, null);   // legacy: falls back to amount
            Add(BookingStatus.Completed, PaymentStatus.Unpaid, 450, null);
            Add(BookingStatus.NoShow, PaymentStatus.Unpaid, 450, null);
            //JJQR - AI (Nov 21, 2026) - a cancelled booking keeps an "Unpaid" payment row, but nothing is owed
            Add(BookingStatus.Cancelled, PaymentStatus.Unpaid, 450, null);
            Add(BookingStatus.Rescheduled, PaymentStatus.Unpaid, 450, null); //JJQR - AI (Nov 21, 2026) - a rescheduled booking owes nothing either
            //JJQR - END
            Add(BookingStatus.Completed, PaymentStatus.Refunded, 450, 450); // refunded is not Paid → not revenue
            await db.SaveChangesAsync();
            return 0;
        });

        using var me = F.ClientFor(userId, "Doctor");
        var rows = await ListOf(await me.GetAsync("/api/reports/daily-booking-summary"));
        var row = Assert.Single(rows);
        Assert.Equal(today.ToString("yyyy-MM-dd"), row.GetProperty("appointment_date").GetString());
        Assert.Equal(7, row.GetProperty("total_bookings").GetInt32());
        Assert.Equal(4, row.GetProperty("completed_count").GetInt32());
        Assert.Equal(2, row.GetProperty("paid_count").GetInt32());
        //JJQR - AI (Nov 21, 2026) - only the completed unpaid visit is unpaid; the no-show and the cancelled booking owe nothing
        Assert.Equal(1, row.GetProperty("unpaid_count").GetInt32());
        //JJQR - END
        Assert.Equal(1, row.GetProperty("no_show_count").GetInt32());
        Assert.Equal(800m, row.GetProperty("revenue").GetDecimal());

        // Doctor B (no bookings) sees nothing; staff still see the clinic-wide view including this doctor's day.
        var (emptyUid, _) = await NewEmptyDoctor();
        using var b = F.ClientFor(emptyUid, "Doctor");
        Assert.Empty(await ListOf(await b.GetAsync("/api/reports/daily-booking-summary")));
        using var staff = StaffC();
        var clinic = (await ListOf(await staff.GetAsync("/api/reports/daily-booking-summary")))
            .Single(r => r.GetProperty("appointment_date").GetString() == today.ToString("yyyy-MM-dd"));
        Assert.True(clinic.GetProperty("total_bookings").GetInt32() >= 5);
    }

    [Fact]
    public async Task Anonymous_and_patient_cannot_read_the_reports()
    {
        await ShouldBe(HttpStatusCode.Unauthorized, await F.Anonymous().GetAsync("/api/reports/daily-booking-summary"));
        await ShouldBe(HttpStatusCode.Forbidden, await F.Patient(W.PatientA).GetAsync("/api/reports/unpaid-completed-visits"));
        await ShouldBe(HttpStatusCode.Forbidden, await F.Patient(W.PatientA).GetAsync("/api/reports/pending-follow-ups"));
    }
}
//JJQR - END
