using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Application.Integrations;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Integrations;

public sealed class IntegrationEventWriter(
    WmsDbContext context,
    IClock clock,
    IRequestContext requestContext,
    IWarehouseContext warehouseContext) : IIntegrationEventWriter
{
    private const int MaximumPayloadLength = 1_000_000;

    public async Task<IntegrationEventPublishResult> EnqueueAsync(
        IntegrationEventDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Validate(draft);

        var payloadJson = IntegrationPayload.Serialize(draft.Payload);
        if (payloadJson.Length > MaximumPayloadLength)
        {
            throw new InvalidOperationException(
                $"Integration event payload exceeds the {MaximumPayloadLength} character limit.");
        }

        var payloadHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)))
            .ToLowerInvariant();
        var eventId = draft.EventId ?? Guid.NewGuid();
        var existing = context.IntegrationOutbox.Local
            .FirstOrDefault(message => message.EventId == eventId)
            ?? await context.IntegrationOutbox.SingleOrDefaultAsync(
                message => message.EventId == eventId,
                cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Integration event '{eventId}' was already enqueued with a different payload.");
            }

            return new IntegrationEventPublishResult(eventId, WasAlreadyEnqueued: true);
        }

        context.IntegrationOutbox.Add(new WmsIntegrationOutboxEntity
        {
            EventId = eventId,
            EventType = draft.EventType.Trim(),
            Version = 1,
            AggregateType = draft.AggregateType.Trim(),
            AggregateKey = draft.AggregateKey.Trim(),
            WarehouseId = draft.WarehouseId ?? warehouseContext.WarehouseId,
            CorrelationId = WmsExecutionIdentifiers.Normalize(
                draft.CorrelationId ?? requestContext.CorrelationId),
            CausationId = WmsExecutionIdentifiers.NormalizeOptional(draft.CausationId),
            OccurredAtUtc = draft.OccurredAtUtc ?? clock.UtcNow,
            CreatedAtUtc = clock.UtcNow,
            PayloadJson = payloadJson,
            PayloadHash = payloadHash,
            Status = WmsIntegrationEventStatuses.Pending,
            AttemptCount = 0
        });

        // Deliberately do not call SaveChangesAsync here. The caller owns the
        // transaction, so the outbox row commits or rolls back with the business mutation.
        return new IntegrationEventPublishResult(eventId, WasAlreadyEnqueued: false);
    }

    private static void Validate(IntegrationEventDraft draft)
    {
        if (!WmsIntegrationEventTypes.IsSupported(draft.EventType))
        {
            throw new ArgumentException(
                $"Unsupported integration event type '{draft.EventType}'.",
                nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.AggregateType))
        {
            throw new ArgumentException("An aggregate type is required.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.AggregateKey))
        {
            throw new ArgumentException("An aggregate key is required.", nameof(draft));
        }
    }
}
