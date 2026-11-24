using ClinicApp.Api.Auditing;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Billing;

//JJQR - AI (Nov 24, 2026) - owner: when the Admin turns on "Mark the visit paid when the doctor completes it" (clinic_settings.auto_paid_on_complete), a visit's
// still-UNPAID payment becomes Paid (cash, exactly the fee the doctor set, no receipt number) as soon as the visit is completed, so the front desk does not have to click "Record payment".
// Runs AFTER the fee is reconciled with the doctor's decision, so a waived visit stays waived and a ₱0 visit is left alone. Off = nothing changes.
public static class AutoPaidOnComplete
{
    public const string Note = "Marked paid automatically when the doctor completed the visit.";

    /// <summary>Marks the booking's unpaid payment as paid when the clinic setting is on. Saves nothing itself; returns the payment id it changed, or null.</summary>
    public static async Task<Guid?> ApplyAsync(ClinicAppDbContext db, Guid bookingId, Guid? actorUserId, DateTimeOffset now, CancellationToken ct)
    {
        var on = await db.ClinicSettings.AsNoTracking().Where(s => s.Id == 1).Select(s => s.AutoPaidOnComplete).SingleOrDefaultAsync(ct);
        if (!on) return null;

        await FeeDecisionReconciler.ReconcileAsync(db, bookingId, now, ct); // waive / align the amount first (tracked entities, saved by the caller)
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment is null || payment.Status != PaymentStatus.Unpaid || payment.Amount <= 0) return null;
        if (PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Paid) is not null) return null;

        payment.Status = PaymentStatus.Paid;
        payment.PaymentMethod = PaymentMethod.Cash;
        payment.AmountReceived = payment.Amount;
        payment.ConfirmNotes = Note;
        payment.ConfirmedByUserId = actorUserId;
        payment.ConfirmedAt = now;
        var booking = await db.Bookings.SingleAsync(b => b.BookingId == bookingId, ct);
        booking.AmountDue = 0;
        booking.UpdatedAt = now;
        AuditLogWriter.Add(db, AuditEntityType.Payment, payment.PaymentId, "Paid (auto on complete)", actorUserId,
            $"₱{payment.Amount:0.##} cash marked paid automatically when the doctor completed the visit (clinic setting).");
        return payment.PaymentId;
    }
}
//JJQR - END
