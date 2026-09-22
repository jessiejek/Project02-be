namespace ClinicApp.Domain;

using ClinicApp.Domain.Enums;

/// <summary>
/// Server-owned booking + payment status transitions (§17.3 #16, P2.1).
/// Controllers must call CanTransition / Ensure before mutating status.
/// </summary>
public static class BookingStatusMachine
{
    /// <summary>Legal next statuses from <paramref name="from"/>.</summary>
    public static IReadOnlySet<BookingStatus> AllowedNext(BookingStatus from) => from switch
    {
        // Pending → NoShow: an online booker who never arrives (queue page "No-show" on a Pending row).
        BookingStatus.Pending => new HashSet<BookingStatus>
        {
            BookingStatus.ProofSubmitted, BookingStatus.Confirmed, BookingStatus.CheckedIn,
            BookingStatus.Cancelled, BookingStatus.Expired, BookingStatus.Rescheduled, BookingStatus.NoShow
        },
        BookingStatus.ProofSubmitted => new HashSet<BookingStatus>
        {
            BookingStatus.Confirmed, BookingStatus.Cancelled, BookingStatus.Expired
        },
        // Confirmed is the pre-queue "booked" state still used by the admin booking page: its
        // "Mark Complete" / "Mark No Show" buttons act on it directly.
        BookingStatus.Confirmed => new HashSet<BookingStatus>
        {
            BookingStatus.CheckedIn, BookingStatus.Cancelled, BookingStatus.Expired, BookingStatus.Rescheduled,
            BookingStatus.Completed, BookingStatus.NoShow
        },
        // CheckedIn → Confirmed is "Undo Check-In" (staff booking page / staff dashboard toggle).
        // CheckedIn → Completed: the doctor's "Complete Consultation" also completes the queue entry, and
        // nothing forces the front desk to have pressed "Call" first.
        BookingStatus.CheckedIn => new HashSet<BookingStatus>
        {
            BookingStatus.InProgress, BookingStatus.OnHold, BookingStatus.Cancelled, BookingStatus.NoShow,
            BookingStatus.Confirmed, BookingStatus.Completed
        },
        // InProgress → NoShow: the queue page offers "No-show" on an in-progress row (patient walked out).
        BookingStatus.InProgress => new HashSet<BookingStatus>
        {
            BookingStatus.OnHold, BookingStatus.Completed, BookingStatus.Cancelled, BookingStatus.NoShow
        },
        BookingStatus.OnHold => new HashSet<BookingStatus>
        {
            BookingStatus.CheckedIn, BookingStatus.InProgress, BookingStatus.Completed,
            BookingStatus.Cancelled, BookingStatus.NoShow
        },
        // Terminal
        BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Expired
            or BookingStatus.NoShow or BookingStatus.Rescheduled
            => new HashSet<BookingStatus>(),
        _ => new HashSet<BookingStatus>()
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        from == to || AllowedNext(from).Contains(to);

    public static string? RejectReason(BookingStatus from, BookingStatus to)
    {
        if (CanTransition(from, to)) return null;
        return $"Illegal booking status transition: {from} → {to}.";
    }
}

public static class PaymentStatusMachine
{
    public static IReadOnlySet<PaymentStatus> AllowedNext(PaymentStatus from) => from switch
    {
        PaymentStatus.Unpaid => new HashSet<PaymentStatus> { PaymentStatus.Paid, PaymentStatus.Waived },
        PaymentStatus.Paid => new HashSet<PaymentStatus> { PaymentStatus.Refunded },
        PaymentStatus.Waived => new HashSet<PaymentStatus>(),
        PaymentStatus.Refunded => new HashSet<PaymentStatus>(),
        _ => new HashSet<PaymentStatus>()
    };

    public static bool CanTransition(PaymentStatus from, PaymentStatus to) =>
        from == to || AllowedNext(from).Contains(to);

    public static string? RejectReason(PaymentStatus from, PaymentStatus to)
    {
        if (CanTransition(from, to)) return null;
        return $"Illegal payment status transition: {from} → {to}.";
    }
}
