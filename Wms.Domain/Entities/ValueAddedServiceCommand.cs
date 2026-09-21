using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class ValueAddedServiceCommand : Entity
{
    private ValueAddedServiceCommand()
    {
    }

    public ValueAddedServiceCommand(
        int orderId,
        string operation,
        string idempotencyKey,
        string requestHash,
        string actorUserId,
        DateTime occurredAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        ValueAddedServiceOrderId = orderId;
        Operation = Required(operation, 80, nameof(operation));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        RequestHash = Required(requestHash, 64, nameof(requestHash));
        ActorUserId = Required(actorUserId, 450, nameof(actorUserId));
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    public int ValueAddedServiceOrderId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ActorUserId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    public ValueAddedServiceOrder Order { get; private set; } = null!;

    private static string Required(string value, int maximumLength, string parameterName)
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
}
