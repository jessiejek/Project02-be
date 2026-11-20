using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 20, 2026) - Rx pad structured fields: nullable "as needed" flag persists on visit Rx lines and on Rx template items (with meal timing + times)
[Collection("api")]
public class RxAsNeededTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");

    private static object Item(string name, bool? asNeeded, string? timing = null, string? meal = null, bool includeAsNeeded = true)
    {
        var d = new Dictionary<string, object?>
        {
            ["medicine_id"] = Guid.NewGuid(), ["generic_name"] = name, ["dosage"] = "2 mg cap", ["quantity"] = "8",
            ["instruction"] = "PO 2 mg after each loose stool", ["is_controlled_substance"] = false,
            ["timing"] = timing, ["meal_relation"] = meal, ["route"] = "PO",
        };
        if (includeAsNeeded) d["as_needed"] = asNeeded;
        return d;
    }

    private static JsonElement Find(JsonElement group, string prop, string name) =>
        group.GetProperty(prop).EnumerateArray().Single(i => i.GetProperty("generic_name").GetString() == name);

    [Fact]
    public async Task Visit_rx_lines_persist_as_needed_true_false_and_legacy_null_with_timing_and_meal()
    {
        using var a = DocA();
        var body = ApiFactory.Body(new
        {
            patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, booking_id = W.BookingA,
            items = new[]
            {
                Item("Loperamide", true),
                Item("Amlodipine", false, "Breakfast", "After"),
                Item("LegacyDrug", null, includeAsNeeded: false),
            },
        });
        var res = await a.PutAsync($"/api/prescription-groups/by-booking/{W.BookingA}", body);
        await ShouldBe(HttpStatusCode.OK, res);
        var saved = await JsonOf(res);
        Assert.True(Find(saved, "prescription_line_items", "Loperamide").GetProperty("as_needed").GetBoolean());
        var amlo = Find(saved, "prescription_line_items", "Amlodipine");
        Assert.False(amlo.GetProperty("as_needed").GetBoolean());
        Assert.Equal("Breakfast", amlo.GetProperty("timing").GetString());
        Assert.Equal("After", amlo.GetProperty("meal_relation").GetString());
        Assert.Equal(JsonValueKind.Null, Find(saved, "prescription_line_items", "LegacyDrug").GetProperty("as_needed").ValueKind);

        // Persisted in the DB, not just echoed.
        var rows = await WithDb(db => db.PrescriptionLineItems.AsNoTracking()
            .Where(i => i.Group.BookingId == W.BookingA).Select(i => new { i.GenericName, i.AsNeeded }).ToListAsync());
        Assert.Equal(true, rows.Single(r => r.GenericName == "Loperamide").AsNeeded);
        Assert.Equal(false, rows.Single(r => r.GenericName == "Amlodipine").AsNeeded);
        Assert.Null(rows.Single(r => r.GenericName == "LegacyDrug").AsNeeded);

        // An amend (replace-all PUT) can flip it.
        var again = await a.PutAsync($"/api/prescription-groups/by-booking/{W.BookingA}", ApiFactory.Body(new
        {
            patient_id = W.PatientA.RowId, doctor_id = W.DoctorA.RowId, booking_id = W.BookingA,
            items = new[] { Item("Loperamide", false, "Dinner", "Before") },
        }));
        await ShouldBe(HttpStatusCode.OK, again);
        var lop = Find(await JsonOf(again), "prescription_line_items", "Loperamide");
        Assert.False(lop.GetProperty("as_needed").GetBoolean());
        Assert.Equal("Dinner", lop.GetProperty("timing").GetString());
    }

    [Fact]
    public async Task Rx_template_items_persist_as_needed_with_timing_and_meal_on_create_and_edit()
    {
        using var a = DocA();
        var res = await a.PostAsync("/api/prescription-templates", ApiFactory.Body(new
        {
            doctor_id = W.DoctorA.RowId, title = "PRN kit", is_system_template = false,
            items = new[] { Item("Loperamide", true), Item("Ascorbic acid", false, "Breakfast", "After"), Item("Old", null, includeAsNeeded: false) },
        }));
        await ShouldBe(HttpStatusCode.OK, res);
        var created = await JsonOf(res);
        var id = created.GetProperty("template_id").GetGuid();
        Assert.True(Find(created, "prescription_template_items", "Loperamide").GetProperty("as_needed").GetBoolean());
        var asc = Find(created, "prescription_template_items", "Ascorbic acid");
        Assert.False(asc.GetProperty("as_needed").GetBoolean());
        Assert.Equal("Breakfast", asc.GetProperty("timing").GetString());
        Assert.Equal(JsonValueKind.Null, Find(created, "prescription_template_items", "Old").GetProperty("as_needed").ValueKind);

        var listed = (await ListOf(await a.GetAsync("/api/prescription-templates"))).Single(t => t.GetProperty("template_id").GetGuid() == id);
        Assert.True(Find(listed, "prescription_template_items", "Loperamide").GetProperty("as_needed").GetBoolean());

        var put = await a.PutAsync($"/api/prescription-templates/{id}", ApiFactory.Body(new
        {
            doctor_id = W.DoctorA.RowId, title = "PRN kit", is_system_template = false,
            items = new[] { Item("Loperamide", false, "Bedtime", "Before") },
        }));
        await ShouldBe(HttpStatusCode.OK, put);
        var edited = Find(await JsonOf(put), "prescription_template_items", "Loperamide");
        Assert.False(edited.GetProperty("as_needed").GetBoolean());
        Assert.Equal("Bedtime", edited.GetProperty("timing").GetString());
        Assert.Equal("Before", edited.GetProperty("meal_relation").GetString());
    }
}
//JJQR - END
