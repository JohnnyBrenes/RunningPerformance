using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RunningPerformance.Infrastructure.Observability;

public sealed class WorkerHeartbeatHealthCheck(OperationalTelemetry telemetry) : IHealthCheck
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var age = telemetry.WorkerHeartbeatAge;
        return Task.FromResult(age <= MaximumAge
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The hosted worker heartbeat is stale."));
    }
}
