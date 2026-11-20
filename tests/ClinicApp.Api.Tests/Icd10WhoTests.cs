using System.Diagnostics;
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 20, 2026) B1/B2/B3
[Collection("api")]
public class Icd10WhoTests(ApiFixture api) : ApiTestBase(api)
{
    private static readonly string[] RequiredWho = ["J45.9", "A97.9", "Z00.1"];
    private static readonly string[] ForbiddenUs = ["J45.909", "Z00.129", "O24.419", "O99.210", "O14.90", "O13.9", "A90"];

    [Fact]
    public async Task After_startup_icd10_catalog_is_the_full_who_list()
    {
        await WithDb(async db =>
        {
            await Icd10Seeder.SeedAsync(db, NullLogger.Instance);
            var codes = await db.Icd10Codes.Select(c => c.Code).ToListAsync();
            Assert.Equal(Icd10Seeder.WhoCodeCount, codes.Count);
            Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var code in RequiredWho)
                Assert.Contains(code, codes);
            foreach (var code in ForbiddenUs)
                Assert.DoesNotContain(code, codes);
            return 0;
        });
    }

    [Fact]
    public async Task Loader_is_idempotent()
    {
        await WithDb(async db =>
        {
            await Icd10Seeder.SeedAsync(db, NullLogger.Instance);
            var first = await db.Icd10Codes.CountAsync();
            await Icd10Seeder.SeedAsync(db, NullLogger.Instance);
            var second = await db.Icd10Codes.CountAsync();
            Assert.Equal(Icd10Seeder.WhoCodeCount, first);
            Assert.Equal(first, second);
            Assert.Equal(first, await db.Icd10Codes.Select(c => c.Code).Distinct().CountAsync());
            return 0;
        });
    }

    [Fact]
    public async Task Loader_remaps_us_codes_on_diagnoses_and_templates()
    {
        var dxId = Guid.NewGuid();
        var tplId = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.ConsultationDiagnoses.Add(new ConsultationDiagnosis
            {
                Id = dxId,
                ConsultationId = W.ConsultationA,
                Icd10Code = "J45.909",
                CustomDescription = "Unspecified asthma, uncomplicated",
                Type = DiagnosisType.Primary,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.DoctorDiagnosisTemplates.Add(new DoctorDiagnosisTemplate
            {
                Id = tplId,
                DoctorId = W.DoctorA.RowId,
                Label = "Asthma",
                Body = "Unspecified asthma, uncomplicated",
                Icd10Code = "J45.909",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await Icd10Seeder.SeedAsync(db, NullLogger.Instance);

            var dx = await db.ConsultationDiagnoses.AsNoTracking().SingleAsync(d => d.Id == dxId);
            Assert.Equal("J45.9", dx.Icd10Code);
            Assert.Equal("Asthma, unspecified", dx.CustomDescription);

            var tpl = await db.DoctorDiagnosisTemplates.AsNoTracking().SingleAsync(t => t.Id == tplId);
            Assert.Equal("J45.9", tpl.Icd10Code);
            Assert.Equal("Asthma, unspecified", tpl.Body);
            return 0;
        });
    }

    [Fact]
    public async Task Search_ranks_code_prefix_and_words_and_caps_at_20()
    {
        using var client = F.Anonymous();

        var j45 = await ListOf(await client.GetAsync("/api/icd10-codes?q=J45"));
        Assert.True(j45.Count <= 20);
        Assert.NotEmpty(j45);
        Assert.All(j45.TakeWhile(e => (e.GetProperty("code").GetString() ?? "").StartsWith("J45", StringComparison.OrdinalIgnoreCase)),
            e => Assert.StartsWith("J45", e.GetProperty("code").GetString(), StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("J45", j45[0].GetProperty("code").GetString(), StringComparison.OrdinalIgnoreCase);

        var j459 = await ListOf(await client.GetAsync("/api/icd10-codes?q=j459"));
        Assert.True(j459.Count <= 20);
        Assert.Contains(j459, e => e.GetProperty("code").GetString() == "J45.9");

        var dengue = await ListOf(await client.GetAsync($"/api/icd10-codes?q={Uri.EscapeDataString("dengue warning")}"));
        Assert.True(dengue.Count <= 20);
        Assert.Contains(dengue, e => e.GetProperty("code").GetString() == "A97.1");
    }

    [Fact]
    public async Task Search_completes_under_one_second_on_full_catalog()
    {
        using var client = F.Anonymous();
        var sw = Stopwatch.StartNew();
        var res = await client.GetAsync("/api/icd10-codes?q=J45");
        sw.Stop();
        await ShouldBe(HttpStatusCode.OK, res);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"ICD-10 search took {sw.Elapsed.TotalMilliseconds:F0} ms");
        var rows = await ListOf(res);
        Assert.True(rows.Count <= 20);
    }
}
//JJQR - END
