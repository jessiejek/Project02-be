using ClinicApp.Api.Hubs;
using ClinicApp.Api.Security;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record ConfirmPaymentRequest(PaymentMethod PaymentMethod, string? ReferenceNumber, string? OrNumber, decimal? AmountReceived, string? ConfirmNotes);
public record WaivePaymentRequest(string Reason);
public record RefundPaymentRequest(decimal Amount, string Reason);

[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(ClinicAppDbContext db, IHubContext<ClinicHub> hub, ActorResolver actors) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Payment>> GetById(Guid id, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();
        return await CanReadAsync(payment.BookingId, ct) ? Ok(payment) : NotFound();
    }

    [HttpGet("booking/{bookingId:guid}")]
    public async Task<ActionResult<Payment>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment is null) return NotFound();
        return await CanReadAsync(payment.BookingId, ct) ? Ok(payment) : NotFound();
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, ConfirmPaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();
        var illegalConfirm = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Paid);
        if (illegalConfirm is not null)
            return BadRequest(new { message = illegalConfirm });

        payment.Status = PaymentStatus.Paid;
        payment.PaymentMethod = request.PaymentMethod;
        payment.ReferenceNumber = request.ReferenceNumber;
        payment.OrNumber = request.OrNumber;
        payment.AmountReceived = request.AmountReceived;
        payment.ConfirmNotes = request.ConfirmNotes;
        payment.ConfirmedByUserId = CurrentUserId();
        payment.ConfirmedAt = DateTimeOffset.UtcNow;

        var booking = await db.Bookings.SingleAsync(b => b.BookingId == payment.BookingId, ct);
        booking.AmountDue = 0;

        await db.SaveChangesAsync(ct);
        await BroadcastAsync(booking.DoctorId, new
        {
            booking_id = booking.BookingId,
            payment_id = payment.PaymentId,
            status = payment.Status.ToString()
        }, ct);
        return Ok(payment);
    }

    /// <summary>Waiving a fee is the doctor's call (contract §17.2 #5, roadmap Phase 4d:
    /// waive = Doctor/Admin). Front-desk Staff may only *record* a waiver the doctor already
    /// decided on the consultation (`pf_decision = 'Waive'`) — they can't waive on their own.</summary>
    [Authorize(Roles = "Admin,Staff,Doctor")]
    [HttpPost("{id:guid}/waive")]
    public async Task<IActionResult> Waive(Guid id, WaivePaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();

        var actor = await actors.ResolveAsync(User, ct);
        var owningBooking = await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == payment.BookingId, ct);
        if (actor.IsDoctor && !actor.ActsAsDoctor(owningBooking.DoctorId)) return Forbid();
        if (actor.IsStaff)
        {
            var doctorDecided = await db.Consultations.AnyAsync(
                c => c.BookingId == payment.BookingId && c.PfDecision == "Waive", ct);
            if (!doctorDecided)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "Only the doctor can waive the professional fee." });
        }
        var illegalWaive = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Waived);
        if (illegalWaive is not null)
            return BadRequest(new { message = illegalWaive });

        payment.Status = PaymentStatus.Waived;
        payment.WaivedReason = request.Reason;
        payment.WaivedByUserId = CurrentUserId();
        payment.WaivedAt = DateTimeOffset.UtcNow;

        var booking = await db.Bookings.SingleAsync(b => b.BookingId == payment.BookingId, ct);
        booking.AmountDue = 0;

        await db.SaveChangesAsync(ct);
        await BroadcastAsync(booking.DoctorId, new
        {
            booking_id = booking.BookingId,
            payment_id = payment.PaymentId,
            status = payment.Status.ToString()
        }, ct);
        return Ok(payment);
    }

    /// <summary>Every real-time push goes to "staff" plus the owning doctor's
    /// own group — PaymentUpdated only ever reached "staff" before, so a
    /// doctor's dashboard/appointments list never got the live push and sat
    /// on stale "Unpaid" until its own fallback poll caught up.</summary>
    private Task BroadcastAsync(Guid doctorId, object payload, CancellationToken ct) =>
        Task.WhenAll(
            hub.Clients.Group("staff").SendAsync("PaymentUpdated", payload, ct),
            hub.Clients.Group($"doctor:{doctorId}").SendAsync("PaymentUpdated", payload, ct));

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, RefundPaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();
        var illegalRefund = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Refunded);
        if (illegalRefund is not null)
            return BadRequest(new { message = illegalRefund });

        payment.Status = PaymentStatus.Refunded;
        payment.RefundAmount = request.Amount;
        payment.RefundReason = request.Reason;
        payment.RefundedByUserId = CurrentUserId();
        payment.RefundedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(payment);
    }

    /// <summary>Own booking's payment for a patient; any payment for staff-like roles.</summary>
    private async Task<bool> CanReadAsync(Guid bookingId, CancellationToken ct)
    {
        var actor = await actors.ResolveAsync(User, ct);
        if (actor.IsStaffLike) return true;
        if (!actor.IsPatient || actor.PatientId is null) return false;
        return await db.Bookings.AnyAsync(b => b.BookingId == bookingId && b.PatientId == actor.PatientId, ct);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
