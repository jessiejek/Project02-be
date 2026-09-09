using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Controllers;

public record ConfirmPaymentRequest(PaymentMethod PaymentMethod, string? ReferenceNumber, string? OrNumber, decimal? AmountReceived, string? ConfirmNotes);
public record WaivePaymentRequest(string Reason);
public record RefundPaymentRequest(decimal Amount, string Reason);

[ApiController]
[Authorize]
[Route("api/payments")]
public class PaymentsController(ClinicAppDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Payment>> GetById(Guid id, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpGet("booking/{bookingId:guid}")]
    public async Task<ActionResult<Payment>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, ConfirmPaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();

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
        return Ok(payment);
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("{id:guid}/waive")]
    public async Task<IActionResult> Waive(Guid id, WaivePaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();

        payment.Status = PaymentStatus.Waived;
        payment.WaivedReason = request.Reason;
        payment.WaivedByUserId = CurrentUserId();
        payment.WaivedAt = DateTimeOffset.UtcNow;

        var booking = await db.Bookings.SingleAsync(b => b.BookingId == payment.BookingId, ct);
        booking.AmountDue = 0;

        await db.SaveChangesAsync(ct);
        return Ok(payment);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> Refund(Guid id, RefundPaymentRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == id, ct);
        if (payment is null) return NotFound();

        payment.Status = PaymentStatus.Refunded;
        payment.RefundAmount = request.Amount;
        payment.RefundReason = request.Reason;
        payment.RefundedByUserId = CurrentUserId();
        payment.RefundedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(payment);
    }

    private Guid? CurrentUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
