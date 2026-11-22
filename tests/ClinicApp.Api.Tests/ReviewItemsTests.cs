//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.1: sync review items (admin list / resolve, desktop upload)
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class ReviewItemsTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<(Guid Id, HttpClient Client)> NodeAsync()
    {
        using var admin = F.As(W.Admin, "Admin");
        var id = Guid.NewGuid();
        var res = await admin.PostAsync("/api/sync/nodes", ApiFactory.Body(new { node_id = id, name = "Review test node", platform = "test", app_version = "0.0.1" }));
        await ShouldBe(HttpStatusCode.Created, res);
        var secret = (await JsonOf(res)).GetProperty("node_secret").GetString()!;
        var client = F.Anonymous();
        var tok = await client.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = id, node_secret = secret }));
        await ShouldBe(HttpStatusCode.OK, tok);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonOf(tok)).GetProperty("access_token").GetString());
        client.DefaultRequestHeaders.Add("X-Sync-Protocol", "1");
        return (id, client);
    }

    private static object Item(Guid id, string kind = "change_rejected", string? entity = null, string? other = null) => new
    {
        id, kind, title = "Consultation was already completed in the cloud", aggregate = "consultation", entity_id = entity ?? Guid.NewGuid().ToString(), other_entity_id = other,
        details = new { reason_code = "finished_immutable" }, created_at = DateTimeOffset.UtcNow,
    };

    private async Task<JsonElement> UploadAsync(HttpClient node, params object[] items)
    {
        var res = await node.PostAsync("/api/sync/review-items", ApiFactory.Body(new { items }));
        await ShouldBe(HttpStatusCode.OK, res);
        return await JsonOf(res);
    }

    [Fact]
    public async Task A_computer_uploads_items_once_and_the_admin_sees_them()
    {
        var (_, node) = await NodeAsync();
        using var _n = node;
        var id = Guid.NewGuid();
        var entity = Guid.NewGuid().ToString();
        var first = await UploadAsync(node, Item(id, "change_rejected", entity));
        Assert.Single(first.GetProperty("accepted").EnumerateArray());

        var again = await UploadAsync(node, Item(id, "change_rejected", entity));                     // the same id again
        Assert.Empty(again.GetProperty("accepted").EnumerateArray());
        Assert.Single(again.GetProperty("skipped").EnumerateArray());
        var same = await UploadAsync(node, Item(Guid.NewGuid(), "change_rejected", entity, "x"));      // another problem on the same entity is its own item
        Assert.Single(same.GetProperty("accepted").EnumerateArray());
        var twin = await UploadAsync(node, Item(Guid.NewGuid(), "change_rejected", entity, "x"));      // but the same problem twice is not listed twice
        Assert.Single(twin.GetProperty("skipped").EnumerateArray());

        using var admin = F.As(W.Admin, "Admin");
        var one = await admin.GetAsync($"/api/sync/review-items/{id}");
        await ShouldBe(HttpStatusCode.OK, one);
        var item = await JsonOf(one);
        Assert.Equal("open", item.GetProperty("status").GetString());
        Assert.Equal("finished_immutable", item.GetProperty("details").GetProperty("reason_code").GetString());
        Assert.NotNull(item.GetProperty("origin_node_id").GetGuid());
    }

    [Fact]
    public async Task A_computer_cannot_upload_the_kinds_the_server_makes_itself_or_junk()
    {
        var (_, node) = await NodeAsync();
        using var _n = node;
        var r = await UploadAsync(node,
            Item(Guid.NewGuid(), "possible_duplicate_patient"),
            Item(Guid.NewGuid(), "or_number_clash"),
            Item(Guid.NewGuid(), "nonsense"),
            new { id = Guid.Empty, kind = "change_stuck", title = "x" },
            new { id = Guid.NewGuid(), kind = "change_stuck", title = "" });
        Assert.Empty(r.GetProperty("accepted").EnumerateArray());
        Assert.Equal(5, r.GetProperty("invalid").GetArrayLength());
        await ShouldBe(HttpStatusCode.BadRequest, await node.PostAsync("/api/sync/review-items", ApiFactory.Body(new { items = Array.Empty<object>() })));
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Doctor")]
    [InlineData("Patient")]
    public async Task Only_an_admin_can_list_open_or_resolve(string role)
    {
        var (_, node) = await NodeAsync();
        using var _n = node;
        var id = Guid.NewGuid();
        await UploadAsync(node, Item(id));
        using var c = role switch { "Staff" => F.As(W.Staff, "Staff"), "Doctor" => F.As(W.DoctorA, "Doctor"), _ => F.Patient(W.PatientA) };
        await ShouldBe(HttpStatusCode.Forbidden, await c.GetAsync("/api/sync/review-items"));
        await ShouldBe(HttpStatusCode.Forbidden, await c.GetAsync($"/api/sync/review-items/{id}"));
        await ShouldBe(HttpStatusCode.Forbidden, await c.PostAsync($"/api/sync/review-items/{id}/resolve", ApiFactory.Body(new { resolution = "dismissed", note = "not mine to close" })));
        await ShouldBe(HttpStatusCode.Forbidden, await c.PostAsync("/api/sync/review-items", ApiFactory.Body(new { items = new[] { Item(Guid.NewGuid()) } })));   // upload is for computers only
        await ShouldBe(HttpStatusCode.Forbidden, await node.GetAsync("/api/sync/review-items"));                                                               // and a computer cannot read the list
    }

    [Fact]
    public async Task Resolving_needs_a_note_a_valid_resolution_and_works_once()
    {
        var (_, node) = await NodeAsync();
        using var _n = node;
        var id = Guid.NewGuid();
        await UploadAsync(node, Item(id));
        using var admin = F.As(W.Admin, "Admin");
        var path = $"/api/sync/review-items/{id}/resolve";
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync(path, ApiFactory.Body(new { resolution = "dismissed", note = "no" })));          // note too short
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync(path, ApiFactory.Body(new { resolution = "merged", note = "merged by hand" }))); // merged only through the merge
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync(path, ApiFactory.Body(new { resolution = "whatever", note = "valid note here" })));

        var ok = await admin.PostAsync(path, ApiFactory.Body(new { resolution = "fixed", note = "Re-entered the amendment on the web." }));
        await ShouldBe(HttpStatusCode.OK, ok);
        var body = await JsonOf(ok);
        Assert.Equal("resolved", body.GetProperty("status").GetString());
        Assert.Equal("fixed", body.GetProperty("resolution").GetString());
        Assert.NotNull(body.GetProperty("resolved_by_user_id").GetGuid());
        await ShouldBe(HttpStatusCode.Conflict, await admin.PostAsync(path, ApiFactory.Body(new { resolution = "dismissed", note = "second try at it" })));
        Assert.True(await WithDb(db => db.AuditLogs.AnyAsync(a => a.EntityId == id && a.Action == "Sync review item resolved")));
    }

    [Fact]
    public async Task The_list_filters_by_status_and_kind_and_pages()
    {
        var (_, node) = await NodeAsync();
        using var _n = node;
        var rejected = Guid.NewGuid(); var stuck = Guid.NewGuid();
        await UploadAsync(node, Item(rejected, "change_rejected"), Item(stuck, "change_stuck"));
        using var admin = F.As(W.Admin, "Admin");
        await ShouldBe(HttpStatusCode.OK, await admin.PostAsync($"/api/sync/review-items/{stuck}/resolve", ApiFactory.Body(new { resolution = "dismissed", note = "known and harmless" })));

        async Task<List<Guid>> Ids(string query)
        {
            var res = await admin.GetAsync("/api/sync/review-items" + query);
            await ShouldBe(HttpStatusCode.OK, res);
            return (await JsonOf(res)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        }
        Assert.Contains(rejected, await Ids("?status=open"));
        Assert.DoesNotContain(stuck, await Ids("?status=open"));
        Assert.Contains(stuck, await Ids("?status=resolved"));
        Assert.Contains(stuck, await Ids("?kind=change_stuck"));
        Assert.DoesNotContain(rejected, await Ids("?kind=change_stuck"));
        Assert.Single(await Ids("?page=1&page_size=1"));
        await ShouldBe(HttpStatusCode.BadRequest, await admin.GetAsync("/api/sync/review-items?status=bogus"));
        await ShouldBe(HttpStatusCode.BadRequest, await admin.GetAsync("/api/sync/review-items?kind=bogus"));
    }
}
//JJQR - END
