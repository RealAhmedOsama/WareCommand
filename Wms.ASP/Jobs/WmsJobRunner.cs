using Microsoft.Extensions.Logging;
using Wms.Application.Context;
using Wms.Application.Jobs;
using Wms.Application.Telemetry;
using Wms.Infrastructure.Jobs;
using Wms.Infrastructure.Logging;

namespace Wms.ASP.Jobs;

public sealed class WmsJobRunner(
    WmsJobHandlerCatalog handlerCatalog,
    IWmsJobExecutionStore executionStore,
    IRequestContext requestContext,
    IWarehouseContext warehouseContext,
    IWmsOperationContextAccessor operationContextAccessor,
    IClock clock,
    WmsBackgroundJobHealthState healthState,
    ILogger<WmsJobRunner> logger)
{
    public Task ExecuteRecurringAsync(
        string jobName,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            WmsJobEnvelope.Create(
                jobName,
                WmsJobCatalog.GetExecutionKey(jobName, clock.UtcNow),
                correlationId: WmsExecutionIdentifiers.NewCorrelationId(),
                actorUserName: "system"),
            cancellationToken);

    public async Task ExecuteAsync(
        WmsJobEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var startedAt = clock.UtcNow;
        var lease = await executionStore.TryStartAsync(envelope, startedAt, cancellationToken);
        if (!lease.ShouldExecute)
        {
            logger.LogDebug(
                "Skipping background job {JobName} with idempotency key {IdempotencyKey}: {SkipReason}",
                envelope.JobName,
                envelope.IdempotencyKey,
                lease.SkipReason);
            return;
        }

        var definition = WmsJobCatalog.Get(envelope.JobName);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(definition.Timeout);
        requestContext.Initialize(envelope.CorrelationId, "BackgroundJob");
        warehouseContext.SetWarehouse(envelope.WarehouseId);
        using var actorScope = WmsActorContext.Begin(new WmsActor(
            envelope.ActorUserId,
            envelope.ActorUserName ?? "system",
            envelope.ActorUserName ?? "System"));
        using var operationScope = WmsLogging.BeginOperation(
            logger,
            operationContextAccessor,
            new WmsOperationContext(
                envelope.CorrelationId,
                WmsExecutionIdentifiers.NewOperationId(),
                "BackgroundJob",
                envelope.JobName,
                envelope.ReferenceId,
                envelope.ActorUserId,
                envelope.WarehouseId));

        var jobContext = new WmsJobContext(envelope, lease.Attempt, startedAt);
        using var activity = WmsTelemetry.ActivitySource.StartActivity(
            $"wms.job.{envelope.JobName}",
            System.Diagnostics.ActivityKind.Internal);
        activity?.SetTag("wms.job.name", envelope.JobName);
        activity?.SetTag("wms.job.queue", definition.Queue);
        activity?.SetTag("wms.job.attempt", lease.Attempt);

        try
        {
            var handler = handlerCatalog.Resolve(envelope.JobName);
            logger.LogInformation(
                Wms.Application.Logging.WmsLogEvents.JobStarted,
                "Background job {JobName} started at attempt {Attempt}",
                envelope.JobName,
                lease.Attempt);
            var result = await handler.ExecuteAsync(jobContext, timeoutSource.Token);
            await executionStore.CompleteAsync(lease, result, clock.UtcNow, cancellationToken);
            healthState.MarkHealthy();
            logger.LogInformation(
                Wms.Application.Logging.WmsLogEvents.JobCompleted,
                "Background job {JobName} completed: {Summary}",
                envelope.JobName,
                result.Summary ?? "No summary.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await executionStore.MarkCanceledAsync(lease, clock.UtcNow, CancellationToken.None);
            logger.LogWarning(
                Wms.Application.Logging.WmsLogEvents.JobFailed,
                "Background job {JobName} canceled during host shutdown",
                envelope.JobName);
            throw;
        }
        catch (WmsPermanentJobException exception)
        {
            await executionStore.MarkDeadLetteredAsync(
                lease,
                exception,
                clock.UtcNow,
                CancellationToken.None);
            await RecordFailureNotificationAsync(lease, exception, "dead-lettered");
            healthState.RecordFailure(envelope.JobName);
            logger.LogError(
                Wms.Application.Logging.WmsLogEvents.JobFailed,
                exception,
                "Permanent background job failure for {JobName}; job was dead-lettered",
                envelope.JobName);
        }
        catch (Exception exception)
        {
            await executionStore.MarkFailedAsync(
                lease,
                exception,
                clock.UtcNow,
                CancellationToken.None);
            await RecordFailureNotificationAsync(lease, exception, "retryable");
            healthState.RecordFailure(envelope.JobName);
            logger.LogError(
                Wms.Application.Logging.WmsLogEvents.JobFailed,
                exception,
                "Retryable background job failure for {JobName}; the Hangfire retry policy will decide the next attempt",
                envelope.JobName);
            throw;
        }
    }

    private async Task RecordFailureNotificationAsync(
        WmsJobExecutionLease lease,
        Exception exception,
        string outcome)
    {
        try
        {
            await executionStore.UpsertNotificationAsync(
                new WmsJobNotification(
                    $"job.failure:{lease.Envelope.JobName}:{lease.Envelope.IdempotencyKey}:{lease.Attempt}",
                    "job.failure",
                    outcome == "dead-lettered" ? "critical" : "error",
                    outcome == "dead-lettered" ? "Background job dead-lettered" : "Background job failed",
                    $"{lease.Envelope.JobName} failed at attempt {lease.Attempt}. Manual retry is available from the secured jobs dashboard.",
                    lease.Envelope.JobName,
                    lease.Envelope.IdempotencyKey,
                    lease.Envelope.CorrelationId,
                    lease.Envelope.WarehouseId,
                    clock.UtcNow.AddDays(30)),
                clock.UtcNow);
        }
        catch (Exception notificationException)
        {
            logger.LogError(
                notificationException,
                "Could not persist the failure notification for background job {JobName}",
                lease.Envelope.JobName);
        }
    }
}
