using System.Data;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Integrations;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Integrations;

public sealed class IntegrationDeadLetterReplayService(
    WmsDbContext context,
    IAuditWriter auditWriter,
    IClock clock) : IIntegrationDeadLetterReplayService
{
    private const int MaximumReasonLength = 250;

    public async Task<Result<IntegrationDeadLetterReplayResult>> ReplayAsync(
        long outboxMessageId,
        string reason,
        string actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken = default)
    {
        if (outboxMessageId <= 0)
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.Validation("integration.replay.id_invalid", "A valid outbox message is required."));
        }

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > MaximumReasonLength)
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.Validation(
                    "integration.replay.reason_invalid",
                    $"Enter a replay reason of 1 to {MaximumReasonLength} characters.",
                    new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["reason"] = [$"Enter a replay reason of 1 to {MaximumReasonLength} characters."]
                    }));
        }

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.Unauthorized("integration.replay.actor_required", "An authenticated operator is required."));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var message = await context.IntegrationOutbox.AsNoTracking()
            .Where(value => value.Id == outboxMessageId)
            .Select(value => new
            {
                value.Id,
                value.EventId,
                value.CorrelationId,
                value.Status,
                value.AttemptCount,
                value.LastError
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (message is null)
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.NotFound("integration.replay.not_found", "The outbox message was not found."));
        }

        if (message.Status != WmsIntegrationEventStatuses.DeadLettered)
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.Conflict(
                    "integration.replay.not_dead_lettered",
                    "Only a dead-lettered outbox message can be replayed."));
        }

        var queuedAtUtc = clock.UtcNow;
        var changedMessageCount = await context.IntegrationOutbox
            .Where(value =>
                value.Id == outboxMessageId &&
                value.Status == WmsIntegrationEventStatuses.DeadLettered)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, WmsIntegrationEventStatuses.Pending)
                .SetProperty(value => value.DeadLetteredAtUtc, (DateTimeOffset?)null)
                .SetProperty(value => value.LeaseUntilUtc, (DateTimeOffset?)null)
                .SetProperty(value => value.NextAttemptAtUtc, queuedAtUtc)
                .SetProperty(value => value.LastError, (string?)null),
                cancellationToken);
        if (changedMessageCount != 1)
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.Conflict(
                    "integration.replay.already_requeued",
                    "The outbox message changed before replay could be queued."));
        }

        var deliveriesQueued = await context.WebhookDeliveries
            .Where(value =>
                value.OutboxMessageId == outboxMessageId &&
                (value.Status == WmsWebhookDeliveryStatuses.DeadLettered ||
                 value.Status == WmsWebhookDeliveryStatuses.Failed))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, WmsWebhookDeliveryStatuses.Failed)
                .SetProperty(value => value.LeaseUntilUtc, (DateTimeOffset?)null)
                .SetProperty(value => value.NextAttemptAtUtc, queuedAtUtc),
                cancellationToken);
        if (deliveriesQueued == 0)
        {
            return Result.Failure<IntegrationDeadLetterReplayResult>(
                WmsErrors.Conflict(
                    "integration.replay.no_failed_deliveries",
                    "The dead-lettered message has no failed delivery to replay."));
        }

        await auditWriter.RecordAsync(new AuditRecord(
            WmsAuditActions.IntegrationDeadLetterReplayed,
            WmsAuditEntityTypes.Integration,
            EntityId: outboxMessageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Before: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = WmsIntegrationEventStatuses.DeadLettered,
                ["attemptCount"] = message.AttemptCount,
                ["lastError"] = message.LastError
            },
            After: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["status"] = WmsIntegrationEventStatuses.Pending,
                ["queuedAtUtc"] = queuedAtUtc,
                ["deliveriesQueued"] = deliveriesQueued,
                ["reason"] = normalizedReason
            },
            ActorUserId: actorUserId,
            ActorUserName: actorUserName,
            Details: "An operator authorized replay of a dead-lettered integration event."),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(new IntegrationDeadLetterReplayResult(
            message.Id,
            message.EventId,
            message.CorrelationId,
            deliveriesQueued,
            queuedAtUtc));
    }
}
