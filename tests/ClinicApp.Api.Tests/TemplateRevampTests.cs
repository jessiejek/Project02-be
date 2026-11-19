using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 19, 2026) - Phase 2 templates revamp: phrase/favorite ordering, Rx template item fields, diagnosis ICD-10, per-doctor ownership on the new endpoints
[Collection("api")]
public class TemplateRevampTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient Admin() => F.As(W.Admin, "Admin");
    private HttpClient StaffC() => F.As(W.Staff, "Staff");
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");
    private HttpClient DocB() => F.As(W.DoctorB, "Doctor");

    private static StringContent Phrase(string field, string label, string body) => ApiFactory.Body(new { field, label, body });

    private async Task<Guid> CreatePhrase(HttpClient c, Guid doctor, string field, string label, string body = "text")
    {
        var res = await c.PostAsync($"/api/soap-phrases?doctorId={doctor}", Phrase(field, label, body));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> PhraseIds(HttpResponseMessage res, string? field = null)
    {
        var rows = await ListOf(res);
        return rows.Where(r => field is null || r.GetProperty("field").GetString() == field).Select(r => r.GetProperty("id").GetGuid()).ToList();
    }

    // ── phrases ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Phrases_are_validated_trimmed_and_appended_in_order()
    {
        using var a = DocA();
        var doc = W.DoctorA.RowId;
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync($"/api/soap-phrases?doctorId={doc}", Phrase("Plan", "  ", "text")));
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync($"/api/soap-phrases?doctorId={doc}", Phrase("Plan", "name", " ")));
        await ShouldBe(HttpStatusCode.BadRequest, await a.PostAsync($"/api/soap-phrases?doctorId={doc}", Phrase("Plan", new string('x', 201), "t")));

        var res = await a.PostAsync($"/api/soap-phrases?doctorId={doc}", Phrase("Subjective", "  zz first  ", "  body one  "));
        await ShouldBe(HttpStatusCode.OK, res);
        var first = await JsonOf(res);
        Assert.Equal("zz first", first.GetProperty("label").GetString());
        Assert.Equal("body one", first.GetProperty("body").GetString());
        var second = await JsonOf(await a.PostAsync($"/api/soap-phrases?doctorId={doc}", Phrase("Subjective", "aa second", "b2")));
        Assert.True(second.GetProperty("sort_order").GetInt32() > first.GetProperty("sort_order").GetInt32());

        // GET honours sort_order before label: "zz first" comes before "aa second".
        var ids = await PhraseIds(await a.GetAsync($"/api/soap-phrases?doctorId={doc}"), "Subjective");
        Assert.True(ids.IndexOf(first.GetProperty("id").GetGuid()) < ids.IndexOf(second.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Phrase_reorder_persists_and_move_to_another_field_goes_to_its_end()
    {
        using var a = DocA();
        var doc = W.DoctorA.RowId;
        var p1 = await CreatePhrase(a, doc, "Objective", "r1");
        var p2 = await CreatePhrase(a, doc, "Objective", "r2");
        var p3 = await CreatePhrase(a, doc, "Objective", "r3");
        await ShouldBe(HttpStatusCode.NoContent, await a.PutAsync($"/api/soap-phrases/order?doctorId={doc}", ApiFactory.Body(new { ids = new[] { p3, p1, p2 } })));
        var ids = await PhraseIds(await a.GetAsync($"/api/soap-phrases?doctorId={doc}"), "Objective");
        Assert.True(ids.IndexOf(p3) < ids.IndexOf(p1) && ids.IndexOf(p1) < ids.IndexOf(p2));

        // Move p3 to Plan: it keeps working through the existing PUT and lands after Plan's existing phrases.
        var planFirst = await CreatePhrase(a, doc, "Plan", "plan-first");
        var moved = await a.PutAsync($"/api/soap-phrases/{p3}", Phrase("Plan", "r3", "text"));
        await ShouldBe(HttpStatusCode.OK, moved);
        var plan = await PhraseIds(await a.GetAsync($"/api/soap-phrases?doctorId={doc}"), "Plan");
        Assert.True(plan.IndexOf(planFirst) < plan.IndexOf(p3));
    }

    [Fact]
    public async Task Phrase_endpoints_enforce_per_doctor_ownership()
    {
        using var a = DocA();
        using var b = DocB();
        var pa = await CreatePhrase(a, W.DoctorA.RowId, "Assessment", "A-private");
        var pb = await CreatePhrase(b, W.DoctorB.RowId, "Assessment", "B-private");

        // B cannot touch A's phrase.
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/soap-phrases/{pa}", Phrase("Assessment", "hijack", "x")));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/soap-phrases/{pa}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PostAsync($"/api/soap-phrases?doctorId={W.DoctorA.RowId}", Phrase("Assessment", "planted", "x")));

        // Reorder: wrong doctor in the query, a foreign id, or a mix → 403 and nothing changes.
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { pa } })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorB.RowId}", ApiFactory.Body(new { ids = new[] { pb, pa } })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorB.RowId}", ApiFactory.Body(new { ids = new[] { Guid.NewGuid() } })));
        Assert.True(await WithDb(db => db.SoapPhrases.AnyAsync(x => x.Id == pa && x.Label == "A-private")));
        Assert.True(await WithDb(db => db.SoapPhrases.AnyAsync(x => x.Id == pb && x.Label == "B-private")));

        // Staff / Patient are not doctors.
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { pa } })));
        using var patient = F.Patient(W.PatientA);
        await ShouldBe(HttpStatusCode.Forbidden, await patient.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { pa } })));

        // Controls: A can reorder own; Admin can act for A.
        await ShouldBe(HttpStatusCode.NoContent, await a.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { pa } })));
        using var admin = Admin();
        await ShouldBe(HttpStatusCode.NoContent, await admin.PutAsync($"/api/soap-phrases/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { pa } })));
        await ShouldBe(HttpStatusCode.NoContent, await a.DeleteAsync($"/api/soap-phrases/{pa}"));
    }

    // ── favorites ─────────────────────────────────────────────────────────
    private async Task<Guid> AddFavorite(HttpClient c, Guid doctor, string name)
    {
        var res = await c.PostAsync($"/api/doctor-favorite-medicines?doctorId={doctor}", ApiFactory.Body(new { medicine_id = Guid.NewGuid(), generic_name = name, dosage = "5mg", quantity = "10" }));
        await ShouldBe(HttpStatusCode.OK, res);
        return (await JsonOf(res)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Favorites_reorder_persists_and_is_owner_only()
    {
        using var a = DocA();
        using var b = DocB();
        var f1 = await AddFavorite(a, W.DoctorA.RowId, "Zeta-fav");
        var f2 = await AddFavorite(a, W.DoctorA.RowId, "Alpha-fav");
        // New favorites append, so Zeta (added first) now sorts before Alpha even though name order says otherwise.
        var order = (await ListOf(await a.GetAsync($"/api/doctor-favorite-medicines?doctorId={W.DoctorA.RowId}"))).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(order.IndexOf(f1) < order.IndexOf(f2));

        await ShouldBe(HttpStatusCode.NoContent, await a.PutAsync($"/api/doctor-favorite-medicines/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { f2, f1 } })));
        order = (await ListOf(await a.GetAsync($"/api/doctor-favorite-medicines?doctorId={W.DoctorA.RowId}"))).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.True(order.IndexOf(f2) < order.IndexOf(f1));

        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/doctor-favorite-medicines/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { f1, f2 } })));
        var fb = await AddFavorite(b, W.DoctorB.RowId, "B-fav");
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/doctor-favorite-medicines/order?doctorId={W.DoctorB.RowId}", ApiFactory.Body(new { ids = new[] { fb, f1 } })));
        using var staff = StaffC();
        await ShouldBe(HttpStatusCode.Forbidden, await staff.PutAsync($"/api/doctor-favorite-medicines/order?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { ids = new[] { f1 } })));
    }

    // ── Rx templates ──────────────────────────────────────────────────────
    private static object RxItem(string name, bool controlled = false, string? timing = "Breakfast,Dinner") => new
    {
        medicine_id = Guid.NewGuid(), generic_name = name, dosage = "500mg", quantity = "20", instruction = "with water",
        is_controlled_substance = controlled, timing, meal_relation = "After", duration_kind = "Days", duration_value = 7,
        indication = "for fever", route = "PO"
    };

    [Fact]
    public async Task Rx_template_items_keep_timing_meal_duration_indication_route_and_controlled_flag()
    {
        using var a = DocA();
        var res = await a.PostAsync("/api/prescription-templates", ApiFactory.Body(new { doctor_id = W.DoctorA.RowId, title = "Fever kit", is_system_template = false, items = new[] { RxItem("Paracetamol"), RxItem("Morphine", controlled: true) } }));
        await ShouldBe(HttpStatusCode.OK, res);
        var created = await JsonOf(res);
        var id = created.GetProperty("template_id").GetGuid();
        var items = created.GetProperty("prescription_template_items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var para = items.Single(i => i.GetProperty("generic_name").GetString() == "Paracetamol");
        Assert.Equal("Breakfast,Dinner", para.GetProperty("timing").GetString());
        Assert.Equal("After", para.GetProperty("meal_relation").GetString());
        Assert.Equal("Days", para.GetProperty("duration_kind").GetString());
        Assert.Equal(7, para.GetProperty("duration_value").GetInt32());
        Assert.Equal("for fever", para.GetProperty("indication").GetString());
        Assert.Equal("PO", para.GetProperty("route").GetString());
        Assert.True(items.Single(i => i.GetProperty("generic_name").GetString() == "Morphine").GetProperty("is_controlled_substance").GetBoolean());

        // Persisted: a fresh GET returns them; an edit (replace-all PUT) keeps the controlled flag and new values.
        var listed = (await ListOf(await a.GetAsync("/api/prescription-templates"))).Single(t => t.GetProperty("template_id").GetGuid() == id);
        Assert.Equal(2, listed.GetProperty("prescription_template_items").GetArrayLength());
        var put = await a.PutAsync($"/api/prescription-templates/{id}", ApiFactory.Body(new { doctor_id = W.DoctorA.RowId, title = "Fever kit v2", is_system_template = false, items = new[] { RxItem("Morphine", controlled: true, timing: "Bedtime") } }));
        await ShouldBe(HttpStatusCode.OK, put);
        var edited = (await JsonOf(put)).GetProperty("prescription_template_items").EnumerateArray().Single();
        Assert.Equal("Bedtime", edited.GetProperty("timing").GetString());
        Assert.True(edited.GetProperty("is_controlled_substance").GetBoolean());
    }

    [Fact]
    public async Task Old_style_rx_template_payload_without_new_fields_still_works()
    {
        using var a = DocA();
        var res = await a.PostAsync("/api/prescription-templates", ApiFactory.Body(new
        {
            doctor_id = W.DoctorA.RowId, title = "Legacy payload", is_system_template = false,
            items = new[] { new { medicine_id = Guid.NewGuid(), generic_name = "Amoxicillin", dosage = "500mg", quantity = "21", instruction = "TID", is_controlled_substance = false } }
        }));
        await ShouldBe(HttpStatusCode.OK, res);
        var item = (await JsonOf(res)).GetProperty("prescription_template_items").EnumerateArray().Single();
        Assert.Equal(JsonValueKind.Null, item.GetProperty("timing").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("duration_value").ValueKind);
    }

    // ── diagnosis templates ───────────────────────────────────────────────
    [Fact]
    public async Task Diagnosis_template_icd10_round_trips_and_old_payload_still_works()
    {
        using var a = DocA();
        var res = await a.PostAsync("/api/doctor-diagnosis-templates", ApiFactory.Body(new { label = "URTI", body = "Acute upper respiratory infection", icd10_code = " J06.9 " }));
        await ShouldBe(HttpStatusCode.OK, res);
        var created = await JsonOf(res);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("J06.9", created.GetProperty("icd10_code").GetString());

        var listed = (await ListOf(await a.GetAsync("/api/doctor-diagnosis-templates"))).Single(t => t.GetProperty("id").GetGuid() == id);
        Assert.Equal("J06.9", listed.GetProperty("icd10_code").GetString());

        // Old payload (no icd10_code) on create works; on edit it clears the code.
        var legacy = await a.PostAsync("/api/doctor-diagnosis-templates", ApiFactory.Body(new { label = "Legacy", body = "Plain text diagnosis" }));
        await ShouldBe(HttpStatusCode.OK, legacy);
        Assert.Equal(JsonValueKind.Null, (await JsonOf(legacy)).GetProperty("icd10_code").ValueKind);
        var put = await a.PutAsync($"/api/doctor-diagnosis-templates/{id}", ApiFactory.Body(new { label = "URTI", body = "Acute upper respiratory infection" }));
        await ShouldBe(HttpStatusCode.OK, put);
        Assert.Equal(JsonValueKind.Null, (await JsonOf(put)).GetProperty("icd10_code").ValueKind);
    }

    [Fact]
    public async Task Diagnosis_and_certificate_templates_are_private_per_doctor()
    {
        using var a = DocA();
        using var b = DocB();
        var dx = (await JsonOf(await a.PostAsync("/api/doctor-diagnosis-templates", ApiFactory.Body(new { label = "A-dx", body = "A diagnosis", icd10_code = "A00" })))).GetProperty("id").GetGuid();
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/doctor-diagnosis-templates/{dx}", ApiFactory.Body(new { label = "x", body = "y", icd10_code = "Z99" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/doctor-diagnosis-templates/{dx}"));
        Assert.False(AnyValue(await ListOf(await b.GetAsync("/api/doctor-diagnosis-templates")), "id", dx));

        var mcRes = await a.PostAsync($"/api/medical-certificate-templates?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { title = "A-cert", is_system_template = false, diagnosis_text = "x", recommendations = "y", purpose_exception = (string?)null }));
        await ShouldBe(HttpStatusCode.OK, mcRes);
        var mc = (await JsonOf(mcRes)).GetProperty("id").GetGuid();
        await ShouldBe(HttpStatusCode.Forbidden, await b.PutAsync($"/api/medical-certificate-templates/{mc}", ApiFactory.Body(new { title = "hijack", is_system_template = false, diagnosis_text = "x", recommendations = "y" })));
        await ShouldBe(HttpStatusCode.Forbidden, await b.DeleteAsync($"/api/medical-certificate-templates/{mc}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.PostAsync($"/api/medical-certificate-templates?doctorId={W.DoctorA.RowId}", ApiFactory.Body(new { title = "planted", is_system_template = false })));
        Assert.False(AnyValue(await ListOf(await b.GetAsync($"/api/medical-certificate-templates?doctorId={W.DoctorB.RowId}")), "id", mc));
    }
}
//JJQR - END
