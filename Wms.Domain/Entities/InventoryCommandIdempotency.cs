using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Durable command-level idempotency state for inventory mutations.
/// The record is deliberately separate from the immutable inventory ledger:
/// it stores the replay contract, not another quantity-changing fact.
/// </summary>
public sealed class InventoryCommandIdempotency : Entity
{
    private InventoryCommandIdempotency()
    {
    }

    public InventoryCommandIdempotency(
        string commandKey,
        string operationType,
        string callerScope,
        string requestHash,
        string correlationId,
        string? actorUserId,
        int? warehouseId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        CommandKey = Required(commandKey, nameof(commandKey), 250);
        OperationType = Required(operationType, nameof(operationType), 100);
        CallerScope = Required(callerScope, nameof(callerScope), 250);
        RequestHash = Required(requestHash, nameof(requestHash), 64);
        CorrelationId = Required(correlationId, nameof(correlationId), 100);
        ActorUserId = Trim(actorUserId, 450);
        WarehouseId = warehouseId;
        Status = InventoryCommandIdempotencyStatus.InProgress;
        StartedAtUtc = Normalize(startedAtUtc);
        ExpiresAtUtc = Normalize(expiresAtUtc);
    }

    public string CommandKey { get; private set; } = string.Empty;
    public string OperationType { get; private set; } = string.Empty;
    public string CallerScope { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public InventoryCommandIdempotencyStatus Status { get; private set; }
    public string? ResultType { get; private set; }
    public string? ResultPayloadJson { get; private set; }
    public string? ResultReference { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? ActorUserId { get; private set; }
    public int? WarehouseId { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public long Revision { get; private set; }

    public bool IsReplayable =>
        Status == InventoryCommandIdempotencyStatus.Succeeded &&
        !string.IsNullOrWhiteSpace(ResultType) &&
        !string.IsNullOrWhiteSpace(ResultPayloadJson);

    public bool IsExpired(DateTimeOffset nowUtc) => ExpiresAtUtc <= nowUtc;

    public void Reclaim(
        string correlationId,
        string? actorUserId,
        int? warehouseId,
        DateTimeOffset startedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (Status is not (InventoryCommandIdempotencyStatus.Failed or InventoryCommandIdempotencyStatus.Expired))
        {
            throw new InvalidOperationException(
                "Only failed or expired inventory commands can be reclaimed.");
        }

        Status = InventoryCommandIdempotencyStatus.InProgress;
        ResultType = null;
        ResultPayloadJson = null;
        ResultReference = null;
        FailureCode = null;
        FailureMessage = null;
        CorrelationId = Required(correlationId, nameof(correlationId), 100);
        ActorUserId = Trim(actorUserId, 450);
        WarehouseId = warehouseId;
        StartedAtUtc = Normalize(startedAtUtc);
        CompletedAtUtc = null;
        ExpiresAtUtc = Normalize(expiresAtUtc);
        Revision++;
        SetUpdatedAt(startedAtUtc.UtcDateTime);
    }

    public void Complete(
        string resultType,
        string resultPayloadJson,
        string? resultReference,
        DateTimeOffset completedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (Status != InventoryCommandIdempotencyStatus.InProgress)
        {
            throw new InvalidOperationException(
                "Only an in-progress inventory command can be completed.");
        }

        ResultType = Required(resultType, nameof(resultType), 200);
        ResultPayloadJson = Required(resultPayloadJson, nameof(resultPayloadJson), 32_000);
        ResultReference = Trim(resultReference, 200);
        FailureCode = null;
        FailureMessage = null;
        Status = InventoryCommandIdempotencyStatus.Succeeded;
        CompletedAtUtc = Normalize(completedAtUtc);
        ExpiresAtUtc = Normalize(expiresAtUtc);
        Revision++;
        SetUpdatedAt(completedAtUtc.UtcDateTime);
    }

    public void MarkFailed(
        string failureCode,
        string? failureMessage,
        DateTimeOffset failedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (Status != InventoryCommandIdempotencyStatus.InProgress)
        {
            throw new InvalidOperationException(
                "Only an in-progress inventory command can be marked failed.");
        }

        Status = InventoryCommandIdempotencyStatus.Failed;
        FailureCode = Required(failureCode, nameof(failureCode), 200);
        FailureMessage = Trim(failureMessage, 2_000);
        ResultType = null;
        ResultPayloadJson = null;
        ResultReference = null;
        CompletedAtUtc = Normalize(failedAtUtc);
        ExpiresAtUtc = Normalize(expiresAtUtc);
        Revision++;
        SetUpdatedAt(failedAtUtc.UtcDateTime);
    }

    public void MarkExpired(DateTimeOffset expiredAtUtc)
    {
        if (Status == InventoryCommandIdempotencyStatus.InProgress && !IsExpired(expiredAtUtc))
        {
            throw new InvalidOperationException(
                "An active inventory command cannot be marked expired before its retention window ends.");
        }

        if (Status == InventoryCommandIdempotencyStatus.Succeeded)
        {
            throw new InvalidOperationException(
                "A succeeded inventory command cannot be marked expired while its replay record is retained.");
        }

        Status = InventoryCommandIdempotencyStatus.Expired;
        CompletedAtUtc ??= Normalize(expiredAtUtc);
        Revision++;
        SetUpdatedAt(expiredAtUtc.UtcDateTime);
    }

    private static string Required(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static DateTimeOffset Normalize(DateTimeOffset value) => value.ToUniversalTime();
}
