//JJQR - AI (2026-11-25) - why: GAV-4 "Not finished on an earlier day": GET /api/queue/leftovers, PUT /api/queue/{id}/move-to-today (race → 409),
// and the existing PUT /api/queue/{id}/no-show on a leftover. Fresh fake patients per test so other tests' bookings never interfere.
using System.Net;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class LeftoverQueueTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");
    private HttpClient DocB() => F.As(W.DoctorB, "Doctor");
    private HttpClient Staff() => F.As(W.Staff, "Staff");

    private Task<Guid> NewPatient() => WithDb(async db =>
    {
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var tag = id.ToString("N")[..8];
        db.Patients.Add(new Patient
        {
            PatientId = id, PatientCode = $"LO-{tag}", FirstName = "Test", LastName = $"Leftover {tag}",
            DateOfBirth = new DateOnly(1990, 1, 1), Sex = SexType.Female, ContactNumber = "09170000000", CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return id;
    });

    private Task<Guid> Leftover(Guid patient, Guid doctor, int daysAgo, BookingStatus status, string queue = "R-011") => WithDb(async db =>
    {
        var id = Guid.NewGuid(); var created = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        db.Bookings.Add(new Booking
        {
            BookingId = id, PatientId = patient, DoctorId = doctor, AppointmentDate = ClinicClock.Today.AddDays(-daysAgo), Status = status,
            PaymentMode = PaymentMode.PayAtClinic, QueueNumber = queue, IsWalkIn = true, ConsultationFeeSnapshot = 450, TotalFee = 450, AmountDue = 450,
            CreatedAt = created, UpdatedAt = created
        });
        db.Payments.Add(new Payment { PaymentId = Guid.NewGuid(), BookingId = id, Amount = 450, Status = PaymentStatus.Unpaid, CreatedAt = created, UpdatedAt = created });
        await db.SaveChangesAsync();
        return id;
    });

    private static async Task<List<string>> Ids(HttpResponseMessage res)
    {
        var json = await JsonOf(res);
        return json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("booking_id").GetString()!).ToList();
    }

    [Fact]
    public async Task Leftovers_list_live_earlier_day_visits_in_each_roles_scope()
    {
        var p = await NewPatient();
        var waiting = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.CheckedIn);
        var inConsult = await Leftover(p, W.DoctorA.RowId, 2, BookingStatus.InProgress);
        var onHold = await Leftover(p, W.DoctorA.RowId, 3, BookingStatus.OnHold);
        var done = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.Completed);
        var noShow = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.NoShow);
        var tooOld = await Leftover(p, W.DoctorA.RowId, 31, BookingStatus.CheckedIn);
        var today = await Leftover(p, W.DoctorA.RowId, 0, BookingStatus.CheckedIn);
        var otherDoctor = await Leftover(p, W.DoctorB.RowId, 1, BookingStatus.CheckedIn);

        using var doc = DocA();
        var res = await doc.GetAsync("/api/queue/leftovers");
        await ShouldBe(HttpStatusCode.OK, res);
        var mine = await Ids(res);
        Assert.Contains(waiting.ToString(), mine);
        Assert.Contains(inConsult.ToString(), mine);
        Assert.Contains(onHold.ToString(), mine);
        foreach (var gone in new[] { done, noShow, tooOld, today, otherDoctor }) Assert.DoesNotContain(gone.ToString(), mine);
        // newest date first
        Assert.True(mine.IndexOf(waiting.ToString()) < mine.IndexOf(inConsult.ToString()));

        using var staff = Staff();
        var all = await Ids(await staff.GetAsync("/api/queue/leftovers"));
        Assert.Contains(otherDoctor.ToString(), all);
        Assert.Contains(waiting.ToString(), all);

        using var patient = F.Patient(W.PatientA);
        await ShouldBeDenied(await patient.GetAsync("/api/queue/leftovers"));
    }

    [Fact]
    public async Task Move_to_today_keeps_the_booking_gives_a_new_number_and_audits()
    {
        var p = await NewPatient();
        var booking = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.InProgress, "R-099");
        var before = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking));
        using var doc = DocA();
        var res = await doc.PutAsync($"/api/queue/{booking}/move-to-today", null);
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await JsonOf(res);

        var after = await WithDb(db => db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == booking));
        Assert.Equal(ClinicClock.Today, after.AppointmentDate);
        Assert.Equal(BookingStatus.InProgress, after.Status);
        Assert.NotEqual("R-099", after.QueueNumber);
        Assert.Equal(after.QueueNumber, body.GetProperty("queue_number").GetString());
        Assert.Equal("R-099", body.GetProperty("previous_queue_number").GetString());
        Assert.Equal(before.RowVersion + 1, after.RowVersion);
        var audit = await WithDb(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityId == booking && a.Action == "Moved to today").ToListAsync());
        Assert.Single(audit);
        Assert.Contains("R-099", audit[0].Details);

        // now on today's board, no longer a leftover; a second move is a 409
        using var staff = Staff();
        var board = await JsonOf(await staff.GetAsync("/api/queue"));
        Assert.Contains(board.GetProperty("items").EnumerateArray(), i => i.GetProperty("booking_id").GetString() == booking.ToString());
        Assert.DoesNotContain(booking.ToString(), await Ids(await doc.GetAsync("/api/queue/leftovers")));
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/queue/{booking}/move-to-today", null));
    }

    [Fact]
    public async Task Move_to_today_at_the_same_moment_wins_once()
    {
        var p = await NewPatient();
        var booking = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.CheckedIn);
        using var a = DocA();
        using var b = Staff();
        using var c = Staff();
        var results = await Task.WhenAll(
            a.PutAsync($"/api/queue/{booking}/move-to-today", null),
            b.PutAsync($"/api/queue/{booking}/move-to-today", null),
            c.PutAsync($"/api/queue/{booking}/move-to-today", null));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await WithDb(db => db.AuditLogs.AsNoTracking().Where(x => x.EntityId == booking && x.Action == "Moved to today").ToListAsync()));
    }

    [Fact]
    public async Task Move_to_today_is_refused_when_not_a_leftover_or_not_yours()
    {
        var p = await NewPatient();
        var done = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.Completed);
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/queue/{done}/move-to-today", null));

        var waiting = await Leftover(p, W.DoctorA.RowId, 2, BookingStatus.CheckedIn);
        using var docB = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await docB.PutAsync($"/api/queue/{waiting}/move-to-today", null));
        using var patient = F.Patient(W.PatientA);
        await ShouldBeDenied(await patient.PutAsync($"/api/queue/{waiting}/move-to-today", null));

        // the same patient already on today's list → 409, nothing changes
        await Leftover(p, W.DoctorA.RowId, 0, BookingStatus.CheckedIn, "R-001");
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/queue/{waiting}/move-to-today", null));
        Assert.Equal(ClinicClock.Today.AddDays(-2), await WithDb(db => db.Bookings.Where(b => b.BookingId == waiting).Select(b => b.AppointmentDate).SingleAsync()));
        Assert.Equal(404, (int)(await doc.PutAsync($"/api/queue/{Guid.NewGuid()}/move-to-today", null)).StatusCode);
    }

    [Fact]
    public async Task No_show_on_a_leftover_uses_the_existing_endpoint()
    {
        var p = await NewPatient();
        var waiting = await Leftover(p, W.DoctorA.RowId, 1, BookingStatus.CheckedIn);
        var inConsult = await Leftover(p, W.DoctorA.RowId, 2, BookingStatus.InProgress);
        using var doc = DocA();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{waiting}/no-show", null));
        using var staff = Staff();
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/queue/{inConsult}/no-show", null));
        var left = await Ids(await doc.GetAsync("/api/queue/leftovers"));
        Assert.DoesNotContain(waiting.ToString(), left);
        Assert.DoesNotContain(inConsult.ToString(), left);
        // a second tap (double tap / two devices) is harmless: still No-show, and a no-show visit can no longer be moved to today
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/queue/{waiting}/no-show", null));
        Assert.Equal(BookingStatus.NoShow, await WithDb(db => db.Bookings.Where(b => b.BookingId == waiting).Select(b => b.Status).SingleAsync()));
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/queue/{waiting}/move-to-today", null));
    }

}
//JJQR - END
