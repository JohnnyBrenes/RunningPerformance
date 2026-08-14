using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RunningPerformance.Infrastructure.Observability;

public sealed class OperationalTelemetry : IDisposable
{
    public const string ActivitySourceName = "RunningPerformance";
    public const string MeterName = "RunningPerformance.Operations";

    private readonly ActivitySource activitySource = new(ActivitySourceName);
    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> apiRequests;
    private readonly Counter<long> apiErrors;
    private readonly Histogram<double> apiDuration;
    private readonly Counter<long> workerJobs;
    private readonly Counter<long> workerErrors;
    private long lastWorkerHeartbeatUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public OperationalTelemetry()
    {
        apiRequests = meter.CreateCounter<long>("running_performance.api.requests");
        apiErrors = meter.CreateCounter<long>("running_performance.api.errors");
        apiDuration = meter.CreateHistogram<double>(
            "running_performance.api.duration",
            unit: "ms");
        workerJobs = meter.CreateCounter<long>("running_performance.worker.jobs");
        workerErrors = meter.CreateCounter<long>("running_performance.worker.errors");
        meter.CreateObservableGauge(
            "running_performance.worker.heartbeat.age",
            () => Math.Max(
                0,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    - Interlocked.Read(ref lastWorkerHeartbeatUnixSeconds)),
            unit: "s");
    }

    public Activity? StartRequest(string method, string route) =>
        activitySource.StartActivity(
            "http.request",
            ActivityKind.Server,
            default(ActivityContext),
            [new("http.request.method", method), new("http.route", route)]);

    public void RecordRequest(double durationMilliseconds, int statusCode)
    {
        var tags = new TagList { { "http.response.status_code", statusCode } };
        apiRequests.Add(1, tags);
        apiDuration.Record(durationMilliseconds, tags);
        if (statusCode >= 500)
        {
            apiErrors.Add(1, tags);
        }
    }

    public void RecordWorkerHeartbeat() =>
        Interlocked.Exchange(
            ref lastWorkerHeartbeatUnixSeconds,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public TimeSpan WorkerHeartbeatAge => TimeSpan.FromSeconds(Math.Max(
        0,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            - Interlocked.Read(ref lastWorkerHeartbeatUnixSeconds)));

    public void RecordWorkerJob(string queue) =>
        workerJobs.Add(1, new TagList { { "job.queue", queue } });

    public void RecordWorkerError() => workerErrors.Add(1);

    public void Dispose()
    {
        activitySource.Dispose();
        meter.Dispose();
    }
}
