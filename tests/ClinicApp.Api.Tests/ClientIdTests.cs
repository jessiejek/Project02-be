//JJQR - AI (Nov 21, 2026) cloud sync: client-supplied ids on REST creates
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class ClientIdTests(ApiFixture api) : ApiTestBase(api)
{
    private Task<Guid> NewPatientAsync() => WithDb(async db =>
    {
        var id = Guid.NewGuid();
        db.Patients.Add(new Patient
        {
            PatientId = id, PatientCode = "MF-C" + id.ToString("N")[..8], FirstName = "Client", LastName = "Id",
            DateOfBirth = new DateOnly(1990, 1, 1), Sex = SexType.Male, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    });

    [Fact]
    public async Task Queue_check_in_uses_the_client_ids_and_a_retry_returns_the_same_ticket()
    {
        var patient = await NewPatientAsync();
        var bookingId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        using var staff = F.As(W.Staff, "Staff");
        var body = new { patient_id = patient, booking_id = bookingId, payment_id = paymentId };
        var first = await staff.PostAsync("/api/queue", ApiFactory.Body(body));
        await ShouldBe(HttpStatusCode.OK, first);
        var ticket = await JsonOf(first);
        Assert.Equal(bookingId, ticket.GetProperty("booking_id").GetGuid());
        Assert.Equal(paymentId, await WithDb(db => db.Payments.Where(p => p.BookingId == bookingId).Select(p => p.PaymentId).SingleAsync()));

        var again = await staff.PostAsync("/api/queue", ApiFactory.Body(body));
        await ShouldBe(HttpStatusCode.OK, again);
        Assert.Equal(ticket.GetProperty("queue_number").GetString(), (await JsonOf(again)).GetProperty("queue_number").GetString());
        Assert.Equal(1, await WithDb(db => db.Bookings.CountAsync(b => b.BookingId == bookingId)));

        var other = await NewPatientAsync();
        var clash = await staff.PostAsync("/api/queue", ApiFactory.Body(new { patient_id = other, booking_id = bookingId }));
        await ShouldBe(HttpStatusCode.Conflict, clash);
        Assert.Contains("id_conflict", await clash.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Booking_create_accepts_booking_and_payment_ids()
    {
        var patient = await NewPatientAsync();
        var bookingId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PostAsync("/api/bookings/walk-in", ApiFactory.Body(new
        {
            patient_id = patient, doctor_id = W.DoctorA.RowId, appointment_date = ClinicApp.Domain.ClinicClock.Today,
            slot_start_time = "09:00:00", slot_end_time = "09:15:00", service_ids = Array.Empty<Guid>(),
            payment_mode = "PayAtClinic", is_walk_in = true, booking_id = bookingId, payment_id = paymentId
        }));
        await ShouldBe(HttpStatusCode.Created, res);
        Assert.Equal(bookingId, (await JsonOf(res)).GetProperty("booking_id").GetGuid());
        Assert.True(await WithDb(db => db.Payments.AnyAsync(p => p.PaymentId == paymentId && p.BookingId == bookingId)));

        // payment id already used → 409
        var res2 = await staff.PostAsync("/api/bookings/walk-in", ApiFactory.Body(new
        {
            patient_id = patient, doctor_id = W.DoctorA.RowId, appointment_date = ClinicApp.Domain.ClinicClock.Today,
            slot_start_time = "09:00:00", slot_end_time = "09:15:00", service_ids = Array.Empty<Guid>(),
            payment_mode = "PayAtClinic", is_walk_in = true, booking_id = Guid.NewGuid(), payment_id = paymentId
        }));
        await ShouldBe(HttpStatusCode.Conflict, res2);
    }

    [Fact]
    public async Task Consultation_and_diagnoses_and_follow_up_take_client_ids()
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db => { (await db.Bookings.SingleAsync(b => b.BookingId == booking)).Status = BookingStatus.InProgress; await db.SaveChangesAsync(); return 0; });
        using var doc = F.As(W.DoctorA, "Doctor");
        var cid = Guid.NewGuid();
        var res = await doc.PutAsync($"/api/consultations/by-booking/{booking}", ApiFactory.Body(new Dictionary<string, object?>
        {
            ["patient_id"] = W.PatientA.RowId, ["doctor_id"] = W.DoctorA.RowId, ["status"] = "Draft",
            ["chief_complaint"] = "cough", ["consultation_id"] = cid
        }));
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.Equal(cid, (await JsonOf(res)).GetProperty("consultation_id").GetGuid());

        var dx = Guid.NewGuid();
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{cid}/diagnoses",
            ApiFactory.Body(new[] { new { id = dx, custom_description = "URTI", type = "Primary" } })));
        Assert.True(await WithDb(db => db.ConsultationDiagnoses.AnyAsync(d => d.Id == dx && d.ConsultationId == cid)));
        // Saving the same list again (same id) still works: the replaced row gets a fresh id.
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/{cid}/diagnoses",
            ApiFactory.Body(new[] { new { id = dx, custom_description = "URTI", type = "Primary" } })));
        Assert.Equal(1, await WithDb(db => db.ConsultationDiagnoses.CountAsync(d => d.ConsultationId == cid)));

        var fu = Guid.NewGuid();
        var fres = await doc.PutAsync($"/api/follow-ups/by-consultation/{cid}", ApiFactory.Body(new
        {
            id = fu, patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId,
            follow_up_date = ClinicApp.Domain.ClinicClock.Today.AddDays(7), reminder_enabled = false, status = "Pending"
        }));
        await ShouldBe(HttpStatusCode.OK, fres);
        Assert.True(await WithDb(db => db.FollowUps.AnyAsync(f => f.Id == fu && f.ConsultationId == cid)));
    }

    [Fact]
    public async Task Entity_payload_create_keeps_a_supplied_id_and_refuses_a_taken_one()
    {
        using var admin = F.As(W.Admin, "Admin");
        var id = Guid.NewGuid();
        var body = new { service_id = id, name = "Client id svc " + id.ToString()[..6], category = "Consultation", price = 100, is_active = true };
        var res = await admin.PostAsync("/api/services", ApiFactory.Body(body));
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        Assert.True(await WithDb(db => db.Services.AnyAsync(s => s.ServiceId == id)));
        await ShouldBe(HttpStatusCode.Conflict, await admin.PostAsync("/api/services", ApiFactory.Body(body)));
    }

    //JJQR - AI (Nov 21, 2026) part B: reschedule's new booking and allergy rows take client ids
    private async Task<Guid> ConfirmedBookingAsync(Guid patient) => await WithDb(async db =>
    {
        var (id, _) = await World.NewBookingAsync(db, patient, W.DoctorA.RowId);
        (await db.Bookings.SingleAsync(b => b.BookingId == id)).Status = BookingStatus.Confirmed;
        await db.SaveChangesAsync();
        return id;
    });

    [Fact]
    public async Task Reschedule_uses_the_client_ids_a_retry_is_idempotent_and_a_clash_is_409()
    {
        var p1 = await NewPatientAsync();
        var p2 = await NewPatientAsync();
        var b1 = await ConfirmedBookingAsync(p1);
        var newId = Guid.NewGuid();
        var newPay = Guid.NewGuid();
        using var staff = F.As(W.Staff, "Staff");
        var body = new { appointment_date = "2099-06-15", reason = "sync", new_booking_id = newId, new_payment_id = newPay };

        var res = await staff.PutAsync($"/api/bookings/{b1}/reschedule", ApiFactory.Body(body));
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.Equal(newId, (await JsonOf(res)).GetProperty("new_booking_id").GetGuid());
        Assert.True(await WithDb(db => db.Payments.AnyAsync(p => p.PaymentId == newPay && p.BookingId == newId)));

        var retry = await staff.PutAsync($"/api/bookings/{b1}/reschedule", ApiFactory.Body(body));
        await ShouldBe(HttpStatusCode.OK, retry);
        Assert.Equal(newId, (await JsonOf(retry)).GetProperty("new_booking_id").GetGuid());
        Assert.Equal(1, await WithDb(db => db.Bookings.CountAsync(b => b.PatientId == p1 && b.AppointmentDate == DateOnly.Parse("2099-06-15"))));

        var b2 = await ConfirmedBookingAsync(p2);
        var clash = await staff.PutAsync($"/api/bookings/{b2}/reschedule", ApiFactory.Body(body));
        await ShouldBe(HttpStatusCode.Conflict, clash);
        Assert.Contains("id_conflict", await clash.Content.ReadAsStringAsync());
        Assert.Equal(BookingStatus.Confirmed, await WithDb(db => db.Bookings.Where(b => b.BookingId == b2).Select(b => b.Status).SingleAsync()));

        // without ids the old behaviour is unchanged
        var b3 = await ConfirmedBookingAsync(p2);
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/bookings/{b3}/reschedule", ApiFactory.Body(new { appointment_date = "2099-06-16" })));
    }

    [Fact]
    public async Task Allergy_replace_takes_client_ids_and_bumps_the_patient_row_version()
    {
        var patient = await NewPatientAsync();
        var before = await WithDb(db => db.Patients.Where(p => p.PatientId == patient).Select(p => p.RowVersion).SingleAsync());
        var a1 = Guid.NewGuid();
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PutAsync($"/api/patients/{patient}/allergies", ApiFactory.Body(new object[]
        {
            new { allergen = "Penicillin", is_nkda = false, id = a1 },
            new { allergen = "Latex", is_nkda = false, id = a1 }, // duplicate id inside one list → the second gets a new id
        }));
        await ShouldBe(HttpStatusCode.OK, res);
        var rows = (await JsonOf(res)).EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(r => r.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.Contains(rows, r => r.GetProperty("id").GetGuid() == a1);
        Assert.True(await WithDb(db => db.Patients.Where(p => p.PatientId == patient).Select(p => p.RowVersion).SingleAsync()) > before);
    }
    //JJQR - END
}
//JJQR - END
