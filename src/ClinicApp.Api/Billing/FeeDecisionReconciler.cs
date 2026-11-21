using ClinicApp.Api.Auditing;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Billing;

//JJQR - AI (Nov 21, 2026) - one rule for "what the doctor decided is what gets collected", applied wherever it can be broken
/// <summary>Makes every still-UNPAID payment follow the doctor's finalised fee decision:
/// a Waive (with a reason) waives the payment; a Charge amount becomes the payment amount, the amount due and the total;
/// anything else just keeps the payment amount equal to the amount due. Paid / Waived / Refunded payments are never touched.
/// Idempotent — a second run changes nothing. Used at startup (heals old records, no manual script needed) and before staff
/// record a payment (so a visit the doctor waived can never be collected). The doctor's save does the same for new visits.</summary>
public static class FeeDecisionReconciler
{
    public const decimal MaxCharge = 100_000m;

    public sealed record Result(int Waived, int AmountsAligned)
    {
        public bool Changed => Waived + AmountsAligned > 0;
    }

    public static async Task<Result> ReconcileAsync(ClinicAppDbContext db, Guid? bookingId, DateTimeOffset now, CancellationToken ct)
    {
        var query =
            from p in db.Payments
            join b in db.Bookings on p.BookingId equals b.BookingId
            join c in db.Consultations.Where(x => x.Status == ConsultationStatus.Completed || x.Status == ConsultationStatus.Amended)
                on b.BookingId equals c.BookingId into cs
            from c in cs.DefaultIfEmpty()
            where p.Status == PaymentStatus.Unpaid
            select new { Payment = p, Booking = b, Consultation = c };
        if (bookingId is { } id) query = query.Where(x => x.Booking.BookingId == id);

        var waived = 0;
        var aligned = 0;
        foreach (var row in await query.ToListAsync(ct))
        {
            var p = row.Payment;
            var b = row.Booking;
            var c = row.Consultation;

            // the doctor waived it → it is waived
            if (c is { PfDecision: "Waive" } && !string.IsNullOrWhiteSpace(c.PfWaiveReason)
                && PaymentStatusMachine.CanTransition(p.Status, PaymentStatus.Waived))
            {
                p.Status = PaymentStatus.Waived;
                p.WaivedReason = c.PfWaiveReason;
                p.WaivedByUserId = c.CompletedByUserId;
                p.WaivedAt = now;
                b.AmountDue = 0;
                b.UpdatedAt = now;
                AuditLogWriter.Add(db, AuditEntityType.Payment, p.PaymentId, "Waived (doctor decision)", null,
                    "Professional fee waived on the doctor's decision (reconciled automatically).");
                waived++;
                continue;
            }

            // the doctor's charge amount is what is collected
            var changed = false;
            decimal? target;
            if (c is { PfDecision: "Charge", PfAmount: > 0 and <= MaxCharge })
            {
                target = c.PfAmount;
                if (b.AmountDue != target || b.TotalFee != target)
                {
                    b.AmountDue = target.Value;
                    b.TotalFee = target.Value;
                    b.UpdatedAt = now;
                    changed = true;
                }
            }
            else
            {
                target = b.AmountDue > 0 ? b.AmountDue : null; // no doctor amount: the amount due is the truth
            }

            if (target is { } t && p.Amount != t)
            {
                p.Amount = t;
                changed = true;
            }
            if (changed) aligned++;
        }
        return new Result(waived, aligned);
    }
}
//JJQR - END
