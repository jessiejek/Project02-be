using System.Diagnostics;

namespace ClinicApp.Api.Middleware;

/// <summary>§17.2 — every request gets a correlation id (echoed as X-Request-Id
/// and pushed into the logging scope) and one structured completion log line
/// with method / path / status / elapsed ms.</summary>
public sealed class RequestContextMiddleware(RequestDelegate next, ILogger<RequestContextMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers.TryGetValue(HeaderName, out var incoming) && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Activity.Current?.Id ?? context.TraceIdentifier;

        context.TraceIdentifier = requestId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = requestId;
            return Task.CompletedTask;
        });

        var sw = Stopwatch.StartNew();
        using (logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path.Value ?? "",
        }))
        {
            try
            {
                await next(context);
            }
            finally
            {
                sw.Stop();
                var level = context.Response.StatusCode >= 500 ? LogLevel.Error
                    : context.Response.StatusCode >= 400 ? LogLevel.Warning
                    : LogLevel.Information;
                logger.Log(level, "{Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
                    context.Request.Method, context.Request.Path.Value, context.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        }
    }
}
