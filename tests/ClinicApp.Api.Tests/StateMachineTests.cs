using System.Net;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class StateMachineTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient Staff() => F.As(W.Staff, "Staff");
    private HttpClient Admin() => F.As(W.Admin, "Admin");
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");

    [Theory]
    [InlineData(BookingStatus.Pending, BookingStatus.CheckedIn, true)]
    [InlineData(BookingStatus.CheckedIn, BookingStatus.InProgress, true)]
    [InlineData(BookingStatus.InProgress, BookingStatus.Completed, true)]
    [InlineData(BookingStatus.Completed, BookingStatus.Pending, false)]
    [InlineData(BookingStatus.Completed, BookingStatus.NoShow, false)]
    [InlineData(BookingStatus.Cancelled, BookingStatus.CheckedIn, false)]
    [InlineData(BookingStatus.Pending, BookingStatus.Completed, false)]
    public void Booking_transition_matrix(BookingStatus from, BookingStatus to, bool ok) =>
        Assert.Equal(ok, BookingStatusMachine.CanTransition(from, to));

    [Theory]
    [InlineData(PaymentStatus.Unpaid, PaymentStatus.Paid, true)]
    [InlineData(PaymentStatus.Unpaid, PaymentStatus.Waived, true)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Refunded, true)]
    [InlineData(PaymentStatus.Unpaid, PaymentStatus.Refunded, false)]
    [InlineData(PaymentStatus.Paid, PaymentStatus.Waived, false)]
    [InlineData(PaymentStatus.Waived, PaymentStatus.Paid, false)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Paid, false)]
    public void Payment_transition_matrix(PaymentStatus from, PaymentStatus to, bool ok) =>
        Assert.Equal(ok, PaymentStatusMachine.CanTransition(from, to));

    [Fact]
    public async Task Api_rejects_illegal_booking_status_jump()
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        // Completed → Pending
        using var staff = Staff();
        var res = await staff.PutAsync($"/api/bookings/{booking}/status", ApiFactory.Body(new { status = "Pending" }));
        await ShouldBe(HttpStatusCode.BadRequest, res);
        Assert.Equal(BookingStatus.Completed, await WithDb(db => db.Bookings.Where(b => b.BookingId == booking).Select(b => b.Status).SingleAsync()));
    }

    [Fact]
    public async Task Api_rejects_confirm_after_waive_and_refund_of_unpaid()
    {
        var (_, payment) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/payments/{payment}/waive", ApiFactory.Body(new { reason = "x" })));
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync($"/api/payments/{payment}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 1 })));
        Assert.Equal(PaymentStatus.Waived, await WithDb(db => db.Payments.Where(p => p.PaymentId == payment).Select(p => p.Status).SingleAsync()));

        var (_, unpaid) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync($"/api/payments/{unpaid}/refund", ApiFactory.Body(new { amount = 1, reason = "nope" })));
        Assert.Equal(PaymentStatus.Unpaid, await WithDb(db => db.Payments.Where(p => p.PaymentId == unpaid).Select(p => p.Status).SingleAsync()));
    }

    [Fact]
    public async Task Completed_consult_cannot_stay_Completed_while_editing()
    {
        var (booking, _) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        await WithDb(async db =>
        {
            db.Consultations.Add(new ClinicApp.Domain.Entities.Consultation
            {
                ConsultationId = Guid.NewGuid(), BookingId = booking, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId,
                Status = ConsultationStatus.Completed, ChiefComplaint = "fever",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return 0;
        });

        using var doc = DocA();
        var stayCompleted = ApiFactory.Body(new
        {
            patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId,
            status = "Completed", chief_complaint = "fever and cough"
        });
        await ShouldBe(HttpStatusCode.Conflict, await doc.PutAsync($"/api/consultations/by-booking/{booking}", stayCompleted));

        var amend = ApiFactory.Body(new
        {
            patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId,
            status = "Amended", chief_complaint = "fever and cough"
        });
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync($"/api/consultations/by-booking/{booking}", amend));
        Assert.Equal(ConsultationStatus.Amended, await WithDb(db => db.Consultations.Where(c => c.BookingId == booking).Select(c => c.Status).SingleAsync()));
    }

    [Fact]
    public async Task Client_audit_POST_is_gone()
    {
        using var staff = Staff();
        var res = await staff.PostAsync("/api/audit-logs", ApiFactory.Body(new
        {
            entity_type = "Booking", entity_id = Guid.NewGuid(), action = "Invented", details = "nope"
        }));
        await ShouldBe(HttpStatusCode.Gone, res);
    }
}
