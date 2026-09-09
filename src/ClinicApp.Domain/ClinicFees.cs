using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain;

/// <summary>§16.6 — the flat clinic-wide fee schedule. One place both the walk-in
/// check-in (provisional) and the consultation-complete (final) recompute call.</summary>
public static class ClinicFees
{
    public readonly record struct Result(decimal Subtotal, decimal DiscountAmount, decimal Total);

    /// <param name="discountCategory">'Senior' | 'PWD' | null.</param>
    public static Result Compute(ClinicSetting s, VisitType visitType, bool medCert, string? discountCategory)
    {
        var hasDiscount = !string.IsNullOrWhiteSpace(discountCategory);
        var subtotal = hasDiscount
            ? s.FeeSeniorPwd
            : visitType == VisitType.FollowUp
                ? s.FeeFollowUp
                : s.FeeConsultation;
        var total = subtotal + (medCert ? s.FeeMedCert : 0m);
        var discountAmount = hasDiscount ? Math.Max(0m, s.FeeConsultation - subtotal) : 0m;
        return new Result(subtotal, discountAmount, total);
    }
}
