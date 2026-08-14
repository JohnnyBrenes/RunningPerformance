using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RunningPerformance.Infrastructure.Observability;

namespace RunningPerformance.Infrastructure.Jobs;

public sealed class IngestionWorker(
    CsvIngestionQueue csvQueue,
    FitIngestionQueue fitQueue,
    RunningPerformance.Application.Ingestion.HistoricalImportOptions options,
    OperationalTelemetry telemetry,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Hosted ingestion worker started with PostgreSQL lease claiming.");

        while (!stoppingToken.IsCancellationRequested)
        {
            telemetry.RecordWorkerHeartbeat();
            try
            {
                var processedFit = await fitQueue.ProcessNextAsync(stoppingToken);
                var processedCsv = !processedFit && await csvQueue.ProcessNextAsync(stoppingToken);
                var processed = processedFit || processedCsv;
                if (processed)
                {
                    telemetry.RecordWorkerJob(processedFit ? "fit" : "csv");
                }

                telemetry.RecordWorkerHeartbeat();
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                telemetry.RecordWorkerError();
                logger.LogError(exception, "Ingestion queue polling failed; the worker will retry.");
                await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), stoppingToken);
            }
        }
    }
}
