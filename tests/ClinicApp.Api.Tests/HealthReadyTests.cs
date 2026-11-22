//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.2: GET /health/ready
using System.Net;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class HealthReadyTests(ApiFixture api) : ApiTestBase(api)
{
    [Fact]
    public async Task Ready_is_anonymous_and_reports_the_database()
    {
        using var anon = F.Anonymous();
        var res = await anon.GetAsync("/health/ready");
        await ShouldBe(HttpStatusCode.OK, res);
        var json = await JsonOf(res);
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("db_ok").GetBoolean());
        Assert.True(json.GetProperty("db_ms").GetInt32() >= 0);
        Assert.True(json.TryGetProperty("server_time", out _));
    }

    [Fact]
    public async Task Liveness_health_is_unchanged()
    {
        using var anon = F.Anonymous();
        var res = await anon.GetAsync("/health");
        await ShouldBe(HttpStatusCode.OK, res);
    }
}
//JJQR - END
