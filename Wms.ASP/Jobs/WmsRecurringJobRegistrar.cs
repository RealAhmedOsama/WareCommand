using Hangfire;
using Hangfire.Common;
using Wms.Application.Jobs;

namespace Wms.ASP.Jobs;

public sealed class WmsRecurringJobRegistrar(
    IRecurringJobManager recurringJobManager,
    ILogger<WmsRecurringJobRegistrar> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var definition in WmsJobCatalog.All)
        {
            recurringJobManager.AddOrUpdate(
                WmsJobCatalog.GetRecurringId(definition.Name),
                Job.FromExpression<WmsJobRunner>(
                    runner => runner.ExecuteRecurringAsync(definition.Name, CancellationToken.None),
                    definition.Queue),
                definition.Cron,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Utc
                });
            logger.LogInformation(
                "Registered recurring background job {JobName} on queue {Queue} with schedule {Cron}",
                definition.Name,
                definition.Queue,
                definition.Cron);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
