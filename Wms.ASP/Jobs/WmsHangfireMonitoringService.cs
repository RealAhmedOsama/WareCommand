using Hangfire;
using Microsoft.Extensions.Hosting;
using Wms.Application.Telemetry;

namespace Wms.ASP.Jobs;

public sealed class WmsHangfireMonitoringService(
    WmsBackgroundJobHealthState healthState,
    ILogger<WmsHangfireMonitoringService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var statistics = JobStorage.Current.GetMonitoringApi().GetStatistics();
                healthState.SetQueueBacklog(
                    statistics.Enqueued + statistics.Processing + statistics.Scheduled);
                healthState.RegisterRunner();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                healthState.MarkUnhealthy();
                logger.LogError(
                    exception,
                    "Could not read durable background-job storage statistics");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        healthState.MarkUnhealthy();
    }
}
