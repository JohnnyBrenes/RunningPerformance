using System.Diagnostics;
using RunningPerformance.Infrastructure.Observability;

namespace RunningPerformance.Api.Http;

public sealed class RequestTelemetryMiddleware(
    RequestDelegate next,
    OperationalTelemetry telemetry,
    ILogger<RequestTelemetryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var route = context.Request.Path.Value ?? "/";
        using var activity = telemetry.StartRequest(context.Request.Method, route);

        await next(context);

        stopwatch.Stop();
        telemetry.RecordRequest(stopwatch.Elapsed.TotalMilliseconds, context.Response.StatusCode);
        logger.LogInformation(
            "HTTP {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds:F1} ms with correlation {CorrelationId}.",
            context.Request.Method,
            route,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds,
            context.GetCorrelationId());
    }
}
