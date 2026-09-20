using Hangfire;
using Hangfire.States;
using Wms.Application.Context;
using Wms.Application.Jobs;

namespace Wms.ASP.Jobs;

public interface IWmsJobDispatcher
{
    string Enqueue(
        string jobName,
        string idempotencyKey,
        string? actorUserId = null,
        string? actorUserName = null,
        int? warehouseId = null,
        string? referenceId = null);
}

public sealed class WmsHangfireJobDispatcher(
    IBackgroundJobClient backgroundJobClient) : IWmsJobDispatcher
{
    public string Enqueue(
        string jobName,
        string idempotencyKey,
        string? actorUserId = null,
        string? actorUserName = null,
        int? warehouseId = null,
        string? referenceId = null)
    {
        var envelope = WmsJobEnvelope.Create(
            jobName,
            idempotencyKey,
            correlationId: WmsExecutionIdentifiers.NewCorrelationId(),
            actorUserId,
            actorUserName,
            warehouseId,
            referenceId);
        var definition = WmsJobCatalog.Get(jobName);
        return backgroundJobClient.Create<WmsJobRunner>(
            runner => runner.ExecuteAsync(envelope, CancellationToken.None),
            new EnqueuedState(definition.Queue));
    }
}

public sealed class WmsDisabledJobDispatcher : IWmsJobDispatcher
{
    public string Enqueue(
        string jobName,
        string idempotencyKey,
        string? actorUserId = null,
        string? actorUserName = null,
        int? warehouseId = null,
        string? referenceId = null) =>
        throw new InvalidOperationException(
            "Durable background jobs are disabled. Set Wms:Jobs:Enabled=true on a PostgreSQL host to enqueue work.");
}
