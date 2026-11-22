//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.3: a receipt number on two payments becomes a review item naming both
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class OrClashReviewTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<HttpClient> NodeAsync()
    {
        using var admin = F.As(W.Admin, "Admin");
        var id = Guid.NewGuid();
        var res = await admin.PostAsync("/api/sync/nodes", ApiFactory.Body(new { node_id = id, name = "OR test node", platform = "test", app_version = "0.0.1" }));
        await ShouldBe(HttpStatusCode.Created, res);
        var secret = (await JsonOf(res)).GetProperty("node_secret").GetString()!;
        var client = F.Anonymous();
        var tok = await client.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = id, node_secret = secret }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonOf(tok)).GetProperty("access_token").GetString());
        client.DefaultRequestHeaders.Add("X-Sync-Protocol", "1");
        return client;
    }

    private async Task<JsonElement> PushPaidAsync(HttpClient node, Guid payment, Guid booking, string or)
    {
        var row = new Dictionary<string, object?>
        {
            ["payment_id"] = payment, ["booking_id"] = booking, ["amount"] = 450, ["status"] = "Paid", ["payment_method"] = "Cash", ["amount_received"] = 450,
            ["or_number"] = or, ["confirmed_at"] = DateTimeOffset.UtcNow, ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
        };
        var op = new Dictionary<string, object?>
        {
            ["operation_id"] = Guid.NewGuid(), ["aggregate"] = "payment", ["entity_id"] = payment, ["operation_type"] = "create", ["base_row_version"] = null,
            ["created_at"] = DateTimeOffset.UtcNow, ["payload"] = new Dictionary<string, object?> { ["data"] = row, ["changed_fields"] = null },
        };
        var res = await node.PostAsync("/api/sync/push", new StringContent(JsonSerializer.Serialize(new { operations = new[] { op } }), Encoding.UTF8, "application/json"));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("results")[0];
    }

    [Fact]
    public async Task A_pushed_payment_with_a_receipt_number_already_used_creates_one_item_naming_both()
    {
        using var node = await NodeAsync();
        var (bookingA, payA) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        var (bookingB, payB) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        var or = "BK-" + Guid.NewGuid().ToString("N")[..8];
        var first = await PushPaidAsync(node, payA, bookingA, or);
        Assert.Null(first.TryGetProperty("reason_code", out var rc0) && rc0.ValueKind != JsonValueKind.Null ? rc0.GetString() : null);   // the first one is fine
        var second = await PushPaidAsync(node, payB, bookingB, or.ToLowerInvariant());                                                       // same number, other capitals
        Assert.Equal("or_number_clash", second.GetProperty("reason_code").GetString());                                                       // existing behaviour: accepted as printed

        var items = await WithDb(db => db.SyncReviewItems.AsNoTracking().Where(i => i.Kind == "or_number_clash" && (i.EntityId == payA.ToString() || i.EntityId == payB.ToString())).ToListAsync());
        var item = Assert.Single(items);
        Assert.Equal("open", item.Status);
        Assert.Contains(or, item.Title, StringComparison.OrdinalIgnoreCase);
        var d = JsonDocument.Parse(item.DetailsJson!).RootElement;
        Assert.Equal(new[] { payA, payB }.OrderBy(x => x), new[] { d.GetProperty("payment_id").GetGuid(), d.GetProperty("other_payment_id").GetGuid() }.OrderBy(x => x));
        Assert.Equal(new[] { bookingA, bookingB }.OrderBy(x => x), new[] { d.GetProperty("booking_id").GetGuid(), d.GetProperty("other_booking_id").GetGuid() }.OrderBy(x => x));

        // the admin sees it on the list, and sending the same payment again does not add a second one
        await PushPaidAsync(node, payB, bookingB, or.ToLowerInvariant());
        using var admin = F.As(W.Admin, "Admin");
        var list = await admin.GetAsync("/api/sync/review-items?kind=or_number_clash&status=open&page_size=100");
        await ShouldBe(HttpStatusCode.OK, list);
        Assert.Equal(1, (await JsonOf(list)).GetProperty("items").EnumerateArray().Count(e => e.GetProperty("id").GetGuid() == item.Id));
        Assert.Equal(1, await WithDb(db => db.SyncReviewItems.CountAsync(i => i.Kind == "or_number_clash" && (i.EntityId == payA.ToString() || i.EntityId == payB.ToString()))));
    }

    [Fact]
    public async Task Different_receipt_numbers_make_no_item()
    {
        using var node = await NodeAsync();
        var (bookingA, payA) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        var (bookingB, payB) = await WithDb(db => World.NewBookingAsync(db, W.PatientA.RowId, W.DoctorA.RowId));
        var tag = Guid.NewGuid().ToString("N")[..8];
        await PushPaidAsync(node, payA, bookingA, "BK-" + tag + "1");
        await PushPaidAsync(node, payB, bookingB, "BK-" + tag + "2");
        Assert.Equal(0, await WithDb(db => db.SyncReviewItems.CountAsync(i => i.Kind == "or_number_clash" && (i.EntityId == payA.ToString() || i.EntityId == payB.ToString()))));
    }
}
//JJQR - END
