using ClinicApp.Api.Auditing; //JJQR - AI (Nov 21, 2026)
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
public record WaivePaymentRequest(string Reason, string? OrNumber = null); //JJQR - AI (21 Nov 2026) optional OR number recorded with the waiver // JJQR - END
public record RefundPaymentRequest(decimal Amount, string Reason);

[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(ClinicAppDbContext db, IHubContext<ClinicHub> hub, ActorResolver actors) : ControllerBase
{
    //JJQR - AI (Nov 21, 2026) - a visit the doctor waived stays on the front desk's list, marked "Waived", collecting ₱0, until staff confirm it — so staff KNOW it was waived instead of it silently vanishing.
    /// <summary>Completed visits whose fee was waived and that the front desk has not confirmed yet (waivers recorded before this feature were marked confirmed by a migration, so only new ones appear).</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("waived-awaiting-confirmation")]
    public async Task<ActionResult<List<object>>> WaivedAwaitingConfirmation(CancellationToken ct)
    {
        var rows = await db.Bookings.AsNoTracking()
            .Where(b => b.Status == BookingStatus.Completed
                && b.Payment != null && b.Payment.Status == PaymentStatus.Waived && b.Payment.ConfirmedAt == null)
            .OrderByDescending(b => b.AppointmentDate).ThenBy(b => b.QueueNumber)
            .Select(b => new
            {
                booking_id = b.BookingId,
                payment_id = b.Payment!.PaymentId,
                patient_name = b.Patient.FirstName + " " + b.Patient.LastName,
                patient_code = b.Patient.PatientCode,
                queue_number = b.QueueNumber,
                appointment_date = b.AppointmentDate,
                waived_reason = b.Payment.WaivedReason
            })
            .ToListAsync(ct);
        return Ok(rows.Cast<object>().ToList());
    }

    /// <summary>The front desk confirms it has seen the waiver (nothing to collect). Idempotent.</summary>
    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("{id:guid}/confirm-waiver")]
    public async Task<IActionResult> ConfirmWaiver(Guid id, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();
        if (payment.Status != PaymentStatus.Waived)
            return BadRequest(new { message = "Only a waived professional fee can be confirmed this way." });
        if (payment.ConfirmedAt is not null) return Ok(payment);

        payment.ConfirmedByUserId = CurrentUserId();
        payment.ConfirmedAt = DateTimeOffset.UtcNow;
        AuditLogWriter.Add(db, AuditEntityType.Payment, payment.PaymentId, "Waiver confirmed (front desk)", CurrentUserId(),
            "Front desk confirmed the professional fee is waived; ₱0 collected.");
        await db.SaveChangesAsync(ct);

        var doctorId = await db.Bookings.AsNoTracking().Where(b => b.BookingId == payment.BookingId).Select(b => b.DoctorId).SingleAsync(ct);
        await BroadcastAsync(doctorId, new { booking_id = payment.BookingId, payment_id = payment.PaymentId, status = payment.Status.ToString() }, ct);
        return Ok(payment);
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) admin review: OR numbers shared by more than one payment (read-only; clashes accepted from sync nodes show up here)
    [Authorize(Roles = "Admin")]
    [HttpGet("or-clashes")]
    public async Task<IActionResult> OrClashes(CancellationToken ct)
    {
        var keys = await db.Payments.AsNoTracking()
            .Where(p => p.OrNumber != null && p.OrNumber != "")
            .GroupBy(p => p.OrNumber!.ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key).Take(500).ToListAsync(ct);
        if (keys.Count == 0) return Ok(Array.Empty<object>());

        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.OrNumber != null && keys.Contains(p.OrNumber.ToLower()))
            .Select(p => new { p.OrNumber, p.PaymentId, p.BookingId, p.Status, p.Amount, p.ConfirmedAt, p.OriginNodeId, p.CreatedAt })
            .ToListAsync(ct);
        return Ok(rows.GroupBy(r => r.OrNumber!.Trim().ToLower()).OrderBy(g => g.Key).Select(g => new
        {
            or_number = g.OrderBy(r => r.CreatedAt).First().OrNumber,
            count = g.Count(),
            payments = g.OrderBy(r => r.CreatedAt).Select(r => new
            {
                payment_id = r.PaymentId, booking_id = r.BookingId, status = r.Status.ToString(), amount = r.Amount,
                confirmed_at = r.ConfirmedAt, origin_node_id = r.OriginNodeId, origin = r.OriginNodeId is null ? "cloud" : "node"
            })
        }));
    }
    //JJQR - END

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

        //JJQR - AI (Nov 21, 2026) - never collect against a stale record: first make the payment follow the doctor's finalised fee decision. A visit the doctor waived cannot be paid,
        // and the amount staff are checked against is the doctor's charge amount.
        var healed = await ClinicApp.Api.Billing.FeeDecisionReconciler.ReconcileAsync(db, payment.BookingId, DateTimeOffset.UtcNow, ct);
        if (healed.Changed)
        {
            await db.SaveChangesAsync(ct);
            if (payment.Status == PaymentStatus.Waived)
            {
                var doctorId = await db.Bookings.AsNoTracking().Where(b => b.BookingId == payment.BookingId).Select(b => b.DoctorId).SingleAsync(ct);
                await BroadcastAsync(doctorId, new { booking_id = payment.BookingId, payment_id = payment.PaymentId, status = payment.Status.ToString() }, ct);
                return Conflict(new { message = "The doctor waived this professional fee, so there is nothing to collect." });
            }
        }
        //JJQR - END

        var illegalConfirm = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Paid);
        if (illegalConfirm is not null)
            return BadRequest(new { message = illegalConfirm });

        // JJQR - AI (Nov 20, 2026) - B-02: the amount handed over must be a sane number that covers the fee
        // (cash may be more — change is given back — but never below the fee, zero, negative or absurd)
        if (!Enum.IsDefined(request.PaymentMethod))
            return BadRequest(new { message = "Choose a valid payment method." });
        //JJQR - AI (Nov 21, 2026) - EXACT: what is recorded is exactly what the doctor set — not less, and not more with change handed back (an over-amount used to inflate the revenue report)
        if (request.AmountReceived is { } received && received != payment.Amount)
            return BadRequest(new { message = $"Amount received (₱{received:0.##}) must be exactly the amount due (₱{payment.Amount:0.##})." });
        //JJQR - END
        if (request.ReferenceNumber is { Length: > 100 } || request.OrNumber is { Length: > 100 } || request.ConfirmNotes is { Length: > 1000 })
            return BadRequest(new { message = "Reference number, OR number or notes are too long." });
        // JJQR - END

        //JJQR - AI (21 Nov 2026) a duplicate OR number (case-insensitive) is refused with 409 and nothing is saved; nothing has been printed yet, so the user can retry
        var orNumber = string.IsNullOrWhiteSpace(request.OrNumber) ? null : request.OrNumber.Trim();
        if (orNumber is not null)
        {
            var orLower = orNumber.ToLower();
            if (await db.Payments.AsNoTracking().AnyAsync(p => p.PaymentId != id && p.OrNumber != null && p.OrNumber.ToLower() == orLower, ct))
                return Conflict(new { error = "or_number_duplicate", message = $"OR number {orNumber} is already used. Enter a different OR number.", or_number = orNumber });
        }
        //JJQR - END

        payment.Status = PaymentStatus.Paid;
        payment.PaymentMethod = request.PaymentMethod;
        payment.ReferenceNumber = request.ReferenceNumber;
        payment.OrNumber = orNumber; //JJQR - AI (21 Nov 2026) trimmed; blank = none // JJQR - END
        payment.AmountReceived = request.AmountReceived ?? payment.Amount; // JJQR - AI (Nov 20, 2026) - B-02: no amount typed = exact fee
        payment.ConfirmNotes = request.ConfirmNotes;
        payment.ConfirmedByUserId = CurrentUserId();
        payment.ConfirmedAt = DateTimeOffset.UtcNow;

        var booking = await db.Bookings.SingleAsync(b => b.BookingId == payment.BookingId, ct);
        booking.AmountDue = 0;

        //JJQR - AI (21 Nov 2026) a race on the unique OR index maps to the same 409 (nothing saved)
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.ToString().Contains("ix_payments_or_number_cloud", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = "or_number_duplicate", message = $"OR number {orNumber} is already used. Enter a different OR number.", or_number = orNumber });
        }
        //JJQR - END
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

        //JJQR - AI (21 Nov 2026) the OR number sent with a waive is saved on the payment; same duplicate rule as confirm (409, nothing saved)
        var waiveOr = string.IsNullOrWhiteSpace(request.OrNumber) ? null : request.OrNumber.Trim();
        if (waiveOr is { Length: > 100 }) return BadRequest(new { message = "OR number is too long." });
        if (waiveOr is not null)
        {
            var waiveOrLower = waiveOr.ToLower();
            if (await db.Payments.AsNoTracking().AnyAsync(p => p.PaymentId != id && p.OrNumber != null && p.OrNumber.ToLower() == waiveOrLower, ct))
                return Conflict(new { error = "or_number_duplicate", message = $"OR number {waiveOr} is already used. Enter a different OR number.", or_number = waiveOr });
            payment.OrNumber = waiveOr;
        }
        //JJQR - END

        payment.Status = PaymentStatus.Waived;
        payment.WaivedReason = request.Reason;
        payment.WaivedByUserId = CurrentUserId();
        payment.WaivedAt = DateTimeOffset.UtcNow;
        //JJQR - AI (Nov 21, 2026) - staff / admin recording a waiver at the desk have, by doing so, confirmed it; only a doctor's own waiver waits for the front desk to confirm
        if (!actor.IsDoctor)
        {
            payment.ConfirmedByUserId = CurrentUserId();
            payment.ConfirmedAt = payment.WaivedAt;
        }
        //JJQR - END

        var booking = await db.Bookings.SingleAsync(b => b.BookingId == payment.BookingId, ct);
        booking.AmountDue = 0;

        //JJQR - AI (21 Nov 2026) a race on the unique OR index maps to the same 409 (nothing saved)
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (waiveOr is not null && ex.ToString().Contains("ix_payments_or_number_cloud", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { error = "or_number_duplicate", message = $"OR number {waiveOr} is already used. Enter a different OR number.", or_number = waiveOr });
        }
        //JJQR - END
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
