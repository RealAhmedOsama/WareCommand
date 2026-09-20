using Microsoft.EntityFrameworkCore;
using Wms.Application.Jobs;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Jobs;

public sealed class WmsJobExecutionStore(
    WmsDbContext context) : IWmsJobExecutionStore
{
    private static readonly TimeSpan StaleExecutionWindow = TimeSpan.FromHours(1);

    public async Task<WmsJobExecutionLease> TryStartAsync(
        WmsJobEnvelope envelope,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var definition = WmsJobCatalog.Get(envelope.JobName);
        var entity = await context.JobExecutions
            .SingleOrDefaultAsync(
                execution => execution.JobName == envelope.JobName &&
                    execution.IdempotencyKey == envelope.IdempotencyKey,
                cancellationToken);

        if (entity is null)
        {
            entity = new WmsJobExecutionEntity
            {
                JobName = envelope.JobName,
                IdempotencyKey = envelope.IdempotencyKey,
                Queue = definition.Queue,
                Status = WmsJobExecutionStatuses.Running,
                AttemptCount = 1,
                CorrelationId = envelope.CorrelationId,
                ActorUserId = envelope.ActorUserId,
                ActorUserName = envelope.ActorUserName,
                WarehouseId = envelope.WarehouseId,
                CreatedAtUtc = nowUtc,
                StartedAtUtc = nowUtc
            };
            context.JobExecutions.Add(entity);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return ToLease(entity, envelope, shouldExecute: true);
            }
            catch (DbUpdateException)
            {
                context.Entry(entity).State = EntityState.Detached;
                entity = await context.JobExecutions
                    .SingleAsync(
                        execution => execution.JobName == envelope.JobName &&
                            execution.IdempotencyKey == envelope.IdempotencyKey,
                        cancellationToken);
            }
        }

        if (entity.Status == WmsJobExecutionStatuses.Succeeded)
        {
            return ToLease(entity, envelope, shouldExecute: false, "The idempotency key already succeeded.");
        }

        if (entity.Status == WmsJobExecutionStatuses.Running &&
            entity.StartedAtUtc.HasValue &&
            nowUtc - entity.StartedAtUtc.Value < StaleExecutionWindow)
        {
            return ToLease(entity, envelope, shouldExecute: false, "The idempotency key is already running.");
        }

        entity.Status = WmsJobExecutionStatuses.Running;
        entity.AttemptCount++;
        entity.CorrelationId = envelope.CorrelationId;
        entity.ActorUserId = envelope.ActorUserId;
        entity.ActorUserName = envelope.ActorUserName;
        entity.WarehouseId = envelope.WarehouseId;
        entity.StartedAtUtc = nowUtc;
        entity.CompletedAtUtc = null;
        entity.LastErrorType = null;
        entity.LastErrorMessage = null;
        await context.SaveChangesAsync(cancellationToken);
        return ToLease(entity, envelope, shouldExecute: true);
    }

    public async Task CompleteAsync(
        WmsJobExecutionLease lease,
        WmsJobExecutionResult result,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(lease, cancellationToken);
        entity.Status = WmsJobExecutionStatuses.Succeeded;
        entity.CompletedAtUtc = completedAtUtc;
        entity.ResultSummary = Trim(result.Summary, 2_000);
        entity.LastErrorType = null;
        entity.LastErrorMessage = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(
        WmsJobExecutionLease lease,
        Exception exception,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(lease, cancellationToken);
        entity.Status = WmsJobExecutionStatuses.Failed;
        entity.CompletedAtUtc = failedAtUtc;
        SetError(entity, exception);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCanceledAsync(
        WmsJobExecutionLease lease,
        DateTimeOffset canceledAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(lease, cancellationToken);
        entity.Status = WmsJobExecutionStatuses.Canceled;
        entity.CompletedAtUtc = canceledAtUtc;
        entity.LastErrorType = nameof(OperationCanceledException);
        entity.LastErrorMessage = "The job was canceled by the host or shutdown token.";
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkDeadLetteredAsync(
        WmsJobExecutionLease lease,
        Exception exception,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await GetEntityAsync(lease, cancellationToken);
        entity.Status = WmsJobExecutionStatuses.DeadLettered;
        entity.CompletedAtUtc = failedAtUtc;
        SetError(entity, exception);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpsertNotificationAsync(
        WmsJobNotification notification,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.JobNotifications
            .SingleOrDefaultAsync(
                item => item.DeduplicationKey == notification.DeduplicationKey,
                cancellationToken);
        if (entity is null)
        {
            context.JobNotifications.Add(new WmsJobNotificationEntity
            {
                DeduplicationKey = TrimRequired(notification.DeduplicationKey, 250),
                Kind = TrimRequired(notification.Kind, 100),
                Severity = TrimRequired(notification.Severity, 30),
                Title = TrimRequired(notification.Title, 200),
                Message = TrimRequired(notification.Message, 2_000),
                JobName = TrimRequired(notification.JobName, 150),
                JobIdempotencyKey = TrimRequired(notification.JobIdempotencyKey, 250),
                CorrelationId = TrimRequired(notification.CorrelationId, 100),
                WarehouseId = notification.WarehouseId,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = notification.ExpiresAtUtc
            });
        }
        else
        {
            entity.Title = TrimRequired(notification.Title, 200);
            entity.Message = TrimRequired(notification.Message, 2_000);
            entity.ExpiresAtUtc = notification.ExpiresAtUtc;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PruneAsync(
        DateTimeOffset completedBeforeUtc,
        DateTimeOffset notificationBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        var executions = await context.JobExecutions
            .Where(execution =>
                execution.CompletedAtUtc < completedBeforeUtc &&
                execution.Status != WmsJobExecutionStatuses.Running)
            .ExecuteDeleteAsync(cancellationToken);
        var notifications = await context.JobNotifications
            .Where(notification =>
                notification.CreatedAtUtc < notificationBeforeUtc &&
                notification.ResolvedAtUtc.HasValue)
            .ExecuteDeleteAsync(cancellationToken);
        return executions + notifications;
    }

    private async Task<WmsJobExecutionEntity> GetEntityAsync(
        WmsJobExecutionLease lease,
        CancellationToken cancellationToken) =>
        await context.JobExecutions.SingleAsync(
            execution => execution.Id == lease.ExecutionId,
            cancellationToken);

    private static WmsJobExecutionLease ToLease(
        WmsJobExecutionEntity entity,
        WmsJobEnvelope envelope,
        bool shouldExecute,
        string? skipReason = null) =>
        new(entity.Id, envelope, entity.AttemptCount, shouldExecute, skipReason);

    private static void SetError(WmsJobExecutionEntity entity, Exception exception)
    {
        entity.LastErrorType = Trim(exception.GetType().Name, 200);
        entity.LastErrorMessage = Trim(exception.Message, 2_000);
    }

    private static string TrimRequired(string value, int maximumLength) =>
        Trim(value, maximumLength) ?? "unknown";

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
