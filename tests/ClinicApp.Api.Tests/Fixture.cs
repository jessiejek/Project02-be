using System.Net;
using System.Text.Json;

namespace ClinicApp.Api.Tests;

/// <summary>One API + database for the whole suite (env vars are process-global, so a single
/// shared host avoids cross-test config races). Tests must not depend on each other's writes:
/// anything that mutates state creates its own rows first.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public ApiFactory Factory { get; } = new();
    public World W => Factory.World;

    public Task InitializeAsync() => Factory.InitializeAsync();

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

public abstract class ApiTestBase(ApiFixture api)
{
    protected ApiFixture Api => api;
    protected World W => api.W;
    protected ApiFactory F => api.Factory;

    protected static async Task<JsonElement> JsonOf(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>Asserts the status and includes the response body in the failure message.</summary>
    protected static async Task ShouldBe(HttpStatusCode expected, HttpResponseMessage res)
    {
        if (res.StatusCode != expected)
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)res.StatusCode} {res.StatusCode} for {res.RequestMessage?.Method} {res.RequestMessage?.RequestUri?.PathAndQuery}: {await res.Content.ReadAsStringAsync()}");
    }

    /// <summary>Anything other than "you got the data": 403 or 404 (both are safe denials).</summary>
    protected static async Task ShouldBeDenied(HttpResponseMessage res)
    {
        if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return;
        Assert.Fail($"Expected a 403/404 denial but got {(int)res.StatusCode} for {res.RequestMessage?.Method} {res.RequestMessage?.RequestUri?.PathAndQuery}: {await res.Content.ReadAsStringAsync()}");
    }

    /// <summary>Every element of a JSON array response, for "only my rows came back" assertions.</summary>
    protected static async Task<List<JsonElement>> ListOf(HttpResponseMessage res)
    {
        await ShouldBe(HttpStatusCode.OK, res);
        var root = await JsonOf(res);
        var arr = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
        return arr.EnumerateArray().ToList();
    }

    protected static bool AnyValue(IEnumerable<JsonElement> rows, string property, Guid value) =>
        rows.Any(r => r.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String && p.GetGuid() == value);

    protected async Task<T> WithDb<T>(Func<ClinicApp.Infrastructure.ClinicAppDbContext, Task<T>> work)
    {
        using var scope = F.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ClinicApp.Infrastructure.ClinicAppDbContext>());
    }
}
