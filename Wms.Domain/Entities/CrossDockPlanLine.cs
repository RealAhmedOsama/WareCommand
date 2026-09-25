using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class CrossDockPlanLine : Entity
{
    private CrossDockPlanLine()
    {
    }

    public CrossDockPlanLine(
        int sequence,
        int salesOrderId,
        int salesOrderLineId,
        int itemId,
        int customerId,
        int salesOrderLineNumber,
        decimal demandBaseQuantity,
        decimal matchedBaseQuantity,
        string salesOrderDocumentNumber,
        string itemSku,
        string? reason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderLineNumber);
        if (demandBaseQuantity <= 0m || matchedBaseQuantity <= 0m || matchedBaseQuantity > demandBaseQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(matchedBaseQuantity));
        }

        Sequence = sequence;
        SalesOrderId = salesOrderId;
        SalesOrderLineId = salesOrderLineId;
        ItemId = itemId;
        CustomerId = customerId;
        SalesOrderLineNumber = salesOrderLineNumber;
        DemandBaseQuantity = demandBaseQuantity;
        MatchedBaseQuantity = matchedBaseQuantity;
        SalesOrderDocumentNumber = Required(salesOrderDocumentNumber, 50, nameof(salesOrderDocumentNumber));
        ItemSku = Required(itemSku, 50, nameof(itemSku));
        Reason = Optional(reason, 1_000);
        Status = CrossDockPlanLineStatus.Matched;
        Revision = 1;
    }

    public int CrossDockPlanId { get; private set; }
    public int Sequence { get; private set; }
    public int SalesOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ItemId { get; private set; }
    public int CustomerId { get; private set; }
    public int SalesOrderLineNumber { get; private set; }
    public decimal DemandBaseQuantity { get; private set; }
    public decimal MatchedBaseQuantity { get; private set; }
    public CrossDockPlanLineStatus Status { get; private set; }
    public string SalesOrderDocumentNumber { get; private set; } = string.Empty;
    public string ItemSku { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public long Revision { get; private set; }

    public CrossDockPlan Plan { get; private set; } = null!;
    public SalesOrder SalesOrder { get; private set; } = null!;
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Customer Customer { get; private set; } = null!;

    public void MarkReservationPending()
    {
        if (Status == CrossDockPlanLineStatus.Matched)
        {
            Status = CrossDockPlanLineStatus.ReservationPending;
            Revision++;
            SetUpdatedAt();
            return;
        }

        if (Status is not (CrossDockPlanLineStatus.ReservationPending or CrossDockPlanLineStatus.WorkPending))
        {
            throw new InvalidOperationException($"A cross-dock plan line in {Status} cannot be reserved.");
        }
    }

    public void MarkWorkPending()
    {
        if (Status == CrossDockPlanLineStatus.ReservationPending)
        {
            Status = CrossDockPlanLineStatus.WorkPending;
            Revision++;
            SetUpdatedAt();
            return;
        }

        if (Status != CrossDockPlanLineStatus.WorkPending)
        {
            throw new InvalidOperationException($"A cross-dock plan line in {Status} cannot release work.");
        }
    }

    public void MarkCompleted(bool shortPick)
    {
        if (Status == CrossDockPlanLineStatus.Completed ||
            Status == CrossDockPlanLineStatus.ShortPick)
        {
            return;
        }

        if (Status != CrossDockPlanLineStatus.WorkPending)
        {
            throw new InvalidOperationException($"A cross-dock plan line in {Status} cannot complete work.");
        }

        Status = shortPick
            ? CrossDockPlanLineStatus.ShortPick
            : CrossDockPlanLineStatus.Completed;
        Revision++;
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

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }
}
