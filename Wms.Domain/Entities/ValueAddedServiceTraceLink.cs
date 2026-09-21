using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ValueAddedServiceTraceLink : Entity
{
    private ValueAddedServiceTraceLink()
    {
    }

    public ValueAddedServiceTraceLink(
        int orderId,
        int inputLineId,
        int? outputLineId,
        ValueAddedServiceTraceKind kind,
        decimal quantity,
        string idempotencyKey,
        DateTime occurredAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inputLineId);
        if (outputLineId.HasValue && outputLineId.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputLineId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        OrderId = orderId;
        InputLineId = inputLineId;
        OutputLineId = outputLineId;
        Kind = kind;
        Quantity = quantity;
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
    }

    public int OrderId { get; private set; }
    public int InputLineId { get; private set; }
    public int? OutputLineId { get; private set; }
    public ValueAddedServiceTraceKind Kind { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ReversedQuantity { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    public ValueAddedServiceOrder Order { get; private set; } = null!;
    public ValueAddedServiceOrderLine InputLine { get; private set; } = null!;
    public ValueAddedServiceOrderLine? OutputLine { get; private set; }

    public void RecordReversal(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (ReversedQuantity + quantity > Quantity)
        {
            throw new InvalidOperationException("The VAS genealogy reversal exceeds the linked quantity.");
        }

        ReversedQuantity += quantity;
        SetUpdatedAt();
    }

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
