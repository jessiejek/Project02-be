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
    // JJQR - AI (Nov 20, 2026) - B-02: upper sanity bound for a single over-the-counter payment
    private const decimal MaxAmountReceived = 100_000m;
    // JJQR - END

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

        // JJQR - AI (Nov 20, 2026) - B-02: the amount handed over must be a sane number that covers the fee
        // (cash may be more — change is given back — but never below the fee, zero, negative or absurd)
        if (!Enum.IsDefined(request.PaymentMethod))
            return BadRequest(new { message = "Choose a valid payment method." });
        if (request.AmountReceived is { } received)
        {
            if (received <= 0 && payment.Amount > 0)
                return BadRequest(new { message = "Amount received must be greater than zero." });
            if (received < payment.Amount)
                return BadRequest(new { message = $"Amount received (₱{received:0.##}) is less than the amount due (₱{payment.Amount:0.##})." });
            if (received > MaxAmountReceived)
                return BadRequest(new { message = $"Amount received cannot be more than ₱{MaxAmountReceived:N0}." });
        }
        if (request.ReferenceNumber is { Length: > 100 } || request.OrNumber is { Length: > 100 } || request.ConfirmNotes is { Length: > 1000 })
            return BadRequest(new { message = "Reference number, OR number or notes are too long." });
        // JJQR - END

        payment.Status = PaymentStatus.Paid;
        payment.PaymentMethod = request.PaymentMethod;
        payment.ReferenceNumber = request.ReferenceNumber;
        payment.OrNumber = request.OrNumber;
        payment.AmountReceived = request.AmountReceived ?? payment.Amount; // JJQR - AI (Nov 20, 2026) - B-02: no amount typed = exact fee
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

        // JJQR - AI (Nov 20, 2026) - B-38: you can only refund what was paid
        if (request.Amount <= 0)
            return BadRequest(new { message = "Refund amount must be greater than zero." });
        if (request.Amount > payment.Amount)
            return BadRequest(new { message = $"Refund amount cannot be more than the amount paid (₱{payment.Amount:0.##})." });
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "A refund reason is required." });
        if (request.Reason.Length > 1000)
            return BadRequest(new { message = "Refund reason is too long (max 1000 characters)." });
        // JJQR - END

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
