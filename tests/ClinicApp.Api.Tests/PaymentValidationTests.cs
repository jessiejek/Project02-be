// JJQR - AI (Nov 20, 2026) - B-02 / B-38: money amounts are validated on the server
using System.Net;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class PaymentValidationTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<Guid> FreshPayment() => (await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId))).PaymentId;
    private Task<PaymentStatus> StatusOf(Guid id) => WithDb(db => db.Payments.AsNoTracking().Where(p => p.PaymentId == id).Select(p => p.Status).SingleAsync());

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    [InlineData(10)]          // below the ₱450 fee
    [InlineData(449.99)]
    [InlineData(99999999)]    // absurd
    public async Task Confirm_rejects_a_bad_amount_received(double amount)
    {
        var pay = await FreshPayment();
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PostAsync($"/api/payments/{pay}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = amount }));
        await ShouldBe(HttpStatusCode.BadRequest, res);
        Assert.Contains("message", await res.Content.ReadAsStringAsync());
        Assert.Equal(PaymentStatus.Unpaid, await StatusOf(pay));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(500)]     // cash with change
    [InlineData(1000)]
    public async Task Confirm_accepts_the_fee_or_more_in_cash(double amount)
    {
        var pay = await FreshPayment();
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{pay}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = amount })));
        Assert.Equal(PaymentStatus.Paid, await StatusOf(pay));
    }

    [Fact]
    public async Task Confirm_without_an_amount_records_the_exact_fee()
    {
        var pay = await FreshPayment();
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{pay}/confirm", ApiFactory.Body(new { payment_method = "GCash", reference_number = "R1" })));
        var received = await WithDb(db => db.Payments.AsNoTracking().Where(p => p.PaymentId == pay).Select(p => p.AmountReceived).SingleAsync());
        Assert.Equal(450m, received);
    }

    [Theory]
    [InlineData(999999)]
    [InlineData(450.01)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Refund_rejects_zero_negative_or_more_than_was_paid(double amount)
    {
        var pay = await FreshPayment();
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{pay}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 450 })));
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync($"/api/payments/{pay}/refund", ApiFactory.Body(new { amount, reason = "too much" })));
        Assert.Equal(PaymentStatus.Paid, await StatusOf(pay));
    }

    [Fact]
    public async Task Refund_needs_a_reason_and_accepts_the_full_amount_paid()
    {
        var pay = await FreshPayment();
        using var staff = F.As(W.Staff, "Staff");
        await ShouldBe(HttpStatusCode.OK, await staff.PostAsync($"/api/payments/{pay}/confirm", ApiFactory.Body(new { payment_method = "Cash", amount_received = 500 })));
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync($"/api/payments/{pay}/refund", ApiFactory.Body(new { amount = 450, reason = "  " })));
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/payments/{pay}/refund", ApiFactory.Body(new { amount = 450, reason = "patient asked" })));
        Assert.Equal(PaymentStatus.Refunded, await StatusOf(pay));
    }
}
// JJQR - END
