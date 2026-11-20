using System.Net;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 20, 2026) A2 / A3 / A4
[Collection("api")]
public class TemplatesBuiltInTests(ApiFixture api) : ApiTestBase(api)
{
    private HttpClient DocA() => F.As(W.DoctorA, "Doctor");
    private HttpClient DocB() => F.As(W.DoctorB, "Doctor");
    private HttpClient Admin() => F.As(W.Admin, "Admin");

    private async Task<Guid> SeedSystemSoap(string title, string plan)
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.SoapTemplates.Add(new SoapTemplate
            {
                Id = id, DoctorId = W.DoctorA.RowId, Title = title, IsSystemTemplate = true,
                Plan = plan, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            return 0;
        });
        return id;
    }

    private static bool IsSystem(System.Text.Json.JsonElement row) =>
        row.TryGetProperty("is_system_template", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.True;

    [Fact]
    public async Task First_GET_copies_built_ins_into_own_rows_and_hides_system_templates()
    {
        var title = "URI pack " + Guid.NewGuid().ToString("N")[..8];
        var systemId = await SeedSystemSoap(title, "original-plan");
        using var a = DocA();

        var soap = await ListOf(await a.GetAsync("/api/soap-templates"));
        Assert.DoesNotContain(soap, IsSystem);
        Assert.False(AnyValue(soap, "id", systemId));
        Assert.Contains(soap, t => t.GetProperty("title").GetString() == title);

        var rx = await ListOf(await a.GetAsync("/api/prescription-templates"));
        Assert.DoesNotContain(rx, IsSystem);
        Assert.False(AnyValue(rx, "template_id", W.RxTemplateSystem));
        Assert.Contains(rx, t => t.GetProperty("title").GetString() == "System rx");

        var cert = await ListOf(await a.GetAsync("/api/medical-certificate-templates"));
        Assert.DoesNotContain(cert, IsSystem);

        var dx = await ListOf(await a.GetAsync("/api/doctor-diagnosis-templates"));
        Assert.Contains(dx, t => t.GetProperty("label").GetString() == BuiltInTemplateCatalog.Diagnoses[0].Label);

        var phrases = await ListOf(await a.GetAsync($"/api/soap-phrases?doctorId={W.DoctorA.RowId}"));
        Assert.Contains(phrases, t => t.GetProperty("label").GetString() == BuiltInTemplateCatalog.Phrases[0].Label);

        Assert.True(await WithDb(db => db.SoapTemplates.AnyAsync(t => t.Id == systemId && t.IsSystemTemplate)));
    }

    [Fact]
    public async Task Second_GET_does_not_duplicate_copies()
    {
        var title = "HTN pack " + Guid.NewGuid().ToString("N")[..8];
        await SeedSystemSoap(title, "plan");
        using var a = DocA();
        var first = await ListOf(await a.GetAsync("/api/soap-templates"));
        var second = await ListOf(await a.GetAsync("/api/soap-templates"));
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(1, first.Count(t => t.GetProperty("title").GetString() == title));
        Assert.Equal(1, second.Count(t => t.GetProperty("title").GetString() == title));
    }

    [Fact]
    public async Task Built_in_originals_are_unchanged_after_GET_copy()
    {
        var title = "Original pack " + Guid.NewGuid().ToString("N")[..8];
        var systemId = await SeedSystemSoap(title, "do-not-touch");
        using var a = DocA();
        await a.GetAsync("/api/soap-templates");
        await a.GetAsync("/api/prescription-templates");

        await WithDb(async db =>
        {
            var soap = await db.SoapTemplates.AsNoTracking().SingleAsync(t => t.Id == systemId);
            Assert.True(soap.IsSystemTemplate);
            Assert.Equal("do-not-touch", soap.Plan);
            Assert.Equal(W.DoctorA.RowId, soap.DoctorId);
            Assert.Null(soap.SourceBuiltinKey);

            var rx = await db.PrescriptionTemplates.AsNoTracking().SingleAsync(t => t.TemplateId == W.RxTemplateSystem);
            Assert.True(rx.IsSystemTemplate);
            Assert.Equal("System rx", rx.Title);
            Assert.Null(rx.SourceBuiltinKey);
            return 0;
        });
    }

    [Fact]
    public async Task Same_title_already_owned_is_not_duplicated()
    {
        var title = "Mine already " + Guid.NewGuid().ToString("N")[..8];
        await SeedSystemSoap(title, "system-body");
        using var a = DocA();
        await ShouldBe(HttpStatusCode.OK, await a.PostAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}",
            ApiFactory.Body(new { title, is_system_template = false, plan = "her-own-body" })));

        var list = await ListOf(await a.GetAsync("/api/soap-templates"));
        var owned = list.Where(t => t.GetProperty("title").GetString() == title).ToList();
        Assert.Single(owned);
        Assert.Equal("her-own-body", owned[0].GetProperty("plan").GetString());
        Assert.False(IsSystem(owned[0]));
    }

    [Fact]
    public async Task Deleted_copy_is_not_brought_back_by_GET()
    {
        var title = "Delete me " + Guid.NewGuid().ToString("N")[..8];
        await SeedSystemSoap(title, "plan");
        using var a = DocA();
        var copyId = (await ListOf(await a.GetAsync("/api/soap-templates")))
            .Single(t => t.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();
        await ShouldBe(HttpStatusCode.NoContent, await a.DeleteAsync($"/api/soap-templates/{copyId}"));

        var again = await ListOf(await a.GetAsync("/api/soap-templates"));
        Assert.DoesNotContain(again, t => t.GetProperty("title").GetString() == title);
        Assert.False(AnyValue(again, "id", copyId));
    }

    [Fact]
    public async Task Restore_brings_deleted_copy_back_once()
    {
        var title = "Restore me " + Guid.NewGuid().ToString("N")[..8];
        await SeedSystemSoap(title, "plan");
        using var a = DocA();
        var copyId = (await ListOf(await a.GetAsync("/api/soap-templates")))
            .Single(t => t.GetProperty("title").GetString() == title).GetProperty("id").GetGuid();
        await a.DeleteAsync($"/api/soap-templates/{copyId}");

        var res = await a.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=soap", null);
        await ShouldBe(HttpStatusCode.OK, res);
        var body = await JsonOf(res);
        Assert.True(body.GetProperty("restored").GetInt32() >= 1);
        Assert.Equal("soap", body.GetProperty("kind").GetString());
        Assert.Contains(await ListOf(await a.GetAsync("/api/soap-templates")), t => t.GetProperty("title").GetString() == title);

        var again = await a.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=soap", null);
        await ShouldBe(HttpStatusCode.OK, again);
        Assert.Equal(0, (await JsonOf(again)).GetProperty("restored").GetInt32());
    }

    [Fact]
    public async Task Restore_never_overwrites_an_edited_copy()
    {
        var title = "Edited copy " + Guid.NewGuid().ToString("N")[..8];
        await SeedSystemSoap(title, "system-plan");
        using var a = DocA();
        var copy = (await ListOf(await a.GetAsync("/api/soap-templates")))
            .Single(t => t.GetProperty("title").GetString() == title);
        var copyId = copy.GetProperty("id").GetGuid();
        await ShouldBe(HttpStatusCode.OK, await a.PutAsync($"/api/soap-templates/{copyId}", ApiFactory.Body(new
        {
            title, is_system_template = false, plan = "doctor-edited",
        })));

        await a.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=soap", null);
        var after = (await ListOf(await a.GetAsync("/api/soap-templates")))
            .Single(t => t.GetProperty("id").GetGuid() == copyId);
        Assert.Equal("doctor-edited", after.GetProperty("plan").GetString());
        Assert.Equal(1, (await ListOf(await a.GetAsync("/api/soap-templates"))).Count(t => t.GetProperty("title").GetString() == title));
    }

    [Fact]
    public async Task Another_doctor_cannot_restore_or_see_for_a_different_doctorId()
    {
        using var b = DocB();
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/soap-templates?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/prescription-templates?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/medical-certificate-templates?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden, await b.GetAsync($"/api/soap-phrases?doctorId={W.DoctorA.RowId}"));
        await ShouldBe(HttpStatusCode.Forbidden,
            await b.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=soap", null));
        using var a = DocA();
        await ShouldBe(HttpStatusCode.OK,
            await a.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=soap", null));
        using var admin = Admin();
        await ShouldBe(HttpStatusCode.OK,
            await admin.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=phrase", null));
        await ShouldBe(HttpStatusCode.BadRequest,
            await a.PostAsync($"/api/templates/restore-built-ins?doctorId={W.DoctorA.RowId}&kind=favorite", null));
    }

    [Fact]
    public async Task DevDataSeeder_sets_demo_doctor_specialization_to_family_medicine()
    {
        var path = Path.Combine(Path.GetTempPath(), "clinic-seed-" + Guid.NewGuid().ToString("N") + ".db");
        await using var conn = new SqliteConnection("Data Source=" + path);
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<ClinicAppDbContext>()
            .UseSqlite(conn)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new ClinicAppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await DevDataSeeder.SeedAsync(db, new PasswordHasherService(), NullLogger.Instance);
            var specs = await db.Doctors.Select(d => d.Specialization).ToListAsync();
            Assert.NotEmpty(specs);
            Assert.All(specs, s => Assert.Equal("Family Medicine", s));
        }
        try { File.Delete(path); } catch { /* best effort */ }
    }
}
//JJQR - END
