//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.2 (D4): a pushed new patient that matches another live patient is applied anyway and an admin review item is written; nothing is merged
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class DuplicatePatientTests(ApiFixture api) : ApiTestBase(api)
{
    private async Task<HttpClient> NodeAsync()
    {
        using var admin = F.As(W.Admin, "Admin");
        var id = Guid.NewGuid();
        var res = await admin.PostAsync("/api/sync/nodes", ApiFactory.Body(new { node_id = id, name = "Duplicate test node", platform = "test", app_version = "0.0.1" }));
        await ShouldBe(HttpStatusCode.Created, res);
        var secret = (await JsonOf(res)).GetProperty("node_secret").GetString()!;
        var client = F.Anonymous();
        var tok = await client.PostAsync("/api/sync/token", ApiFactory.Body(new { node_id = id, node_secret = secret }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await JsonOf(tok)).GetProperty("access_token").GetString());
        client.DefaultRequestHeaders.Add("X-Sync-Protocol", "1");
        return client;
    }

    private static Dictionary<string, object?> Row(Guid id, string code, string first, string last, string dob) => new()
    {
        ["patient_id"] = id, ["patient_code"] = code, ["first_name"] = first, ["last_name"] = last, ["date_of_birth"] = dob,
        ["sex"] = "Female", ["email"] = "", ["created_at"] = DateTimeOffset.UtcNow, ["updated_at"] = DateTimeOffset.UtcNow,
    };

    private async Task<JsonElement> PushPatientAsync(HttpClient node, Dictionary<string, object?> row, Guid? opId = null)
    {
        var op = new Dictionary<string, object?>
        {
            ["operation_id"] = opId ?? Guid.NewGuid(), ["aggregate"] = "patient", ["entity_id"] = row["patient_id"], ["operation_type"] = "create",
            ["base_row_version"] = null, ["created_at"] = DateTimeOffset.UtcNow,
            ["payload"] = new Dictionary<string, object?> { ["data"] = row, ["changed_fields"] = null },
        };
        var res = await node.PostAsync("/api/sync/push", new StringContent(JsonSerializer.Serialize(new { operations = new[] { op } }), Encoding.UTF8, "application/json"));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("results")[0];
    }

    private Task<List<SyncReviewItem>> ItemsAsync(Guid newPatient) =>
        WithDb(db => db.SyncReviewItems.AsNoTracking().Where(i => i.Kind == "possible_duplicate_patient" && i.PatientId == newPatient).ToListAsync());

    [Fact]
    public async Task A_matching_new_patient_is_applied_and_an_item_is_written_without_any_merge()
    {
        using var node = await NodeAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await PushPatientAsync(node, Row(first, "MF-D-" + tag + "1", "Maria", "De La Cruz" + tag, "1980-03-04"));
        var r = await PushPatientAsync(node, Row(second, "MF-D-" + tag + "2", "  MARÍA ", "dela-cruz" + tag, "1980-03-04"));   // capitals, accent, spaces, hyphen all ignored

        Assert.NotEqual("rejected", r.GetProperty("status").GetString());
        Assert.Equal(2, await WithDb(db => db.Patients.CountAsync(p => p.PatientId == first || p.PatientId == second)));   // both exist: never merged or dropped
        var item = Assert.Single(await ItemsAsync(second));
        Assert.Equal(first, item.OtherPatientId);
        Assert.Equal("open", item.Status);
        Assert.Contains("Possible duplicate patient", item.Title);
        Assert.Equal(second.ToString(), item.EntityId);
    }

    [Fact]
    public async Task A_different_birthdate_or_name_is_not_flagged()
    {
        using var node = await NodeAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        await PushPatientAsync(node, Row(Guid.NewGuid(), "MF-E-" + tag + "1", "Lola", "Same" + tag, "1975-01-01"));
        var otherDob = Guid.NewGuid(); var otherName = Guid.NewGuid();
        await PushPatientAsync(node, Row(otherDob, "MF-E-" + tag + "2", "Lola", "Same" + tag, "1975-01-02"));
        await PushPatientAsync(node, Row(otherName, "MF-E-" + tag + "3", "Lolo", "Same" + tag, "1975-01-01"));
        Assert.Empty(await ItemsAsync(otherDob));
        Assert.Empty(await ItemsAsync(otherName));
    }

    [Fact]
    public async Task Pushing_the_same_patient_again_makes_no_second_item()
    {
        using var node = await NodeAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var op = Guid.NewGuid();
        await PushPatientAsync(node, Row(first, "MF-F-" + tag + "1", "Rosa", "Repeat" + tag, "1990-06-06"));
        await PushPatientAsync(node, Row(second, "MF-F-" + tag + "2", "Rosa", "Repeat" + tag, "1990-06-06"), op);
        await PushPatientAsync(node, Row(second, "MF-F-" + tag + "2", "Rosa", "Repeat" + tag, "1990-06-06"), op);          // a retry of the same operation
        await PushPatientAsync(node, Row(second, "MF-F-" + tag + "2", "Rosa", "Repeat" + tag, "1990-06-06"));               // the same patient sent again (an update, not a new patient)
        Assert.Single(await ItemsAsync(second));
    }

    [Fact]
    public async Task A_deleted_patient_is_not_a_duplicate()
    {
        using var node = await NodeAsync();
        var tag = Guid.NewGuid().ToString("N")[..6];
        var gone = Guid.NewGuid(); var fresh = Guid.NewGuid();
        await PushPatientAsync(node, Row(gone, "MF-G-" + tag + "1", "Elena", "Gone" + tag, "1985-02-02"));
        await WithDb(async db => { var p = await db.Patients.SingleAsync(x => x.PatientId == gone); p.DeletedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return 0; });
        await PushPatientAsync(node, Row(fresh, "MF-G-" + tag + "2", "Elena", "Gone" + tag, "1985-02-02"));
        Assert.Empty(await ItemsAsync(fresh));
    }

    [Theory]
    [InlineData("María", "maria")]
    [InlineData("De La Cruz", "dela-cruz")]
    [InlineData(" Ana  Marie ", "ANA MARIE")]
    [InlineData("O'Brien", "obrien")]
    public void Names_are_compared_without_capitals_accents_spaces_or_punctuation(string a, string b) => Assert.Equal(PersonNameKey.Of(a), PersonNameKey.Of(b));

    [Fact]
    public void Different_names_stay_different() => Assert.NotEqual(PersonNameKey.Of("Maria"), PersonNameKey.Of("Mario"));
}
//JJQR - END
