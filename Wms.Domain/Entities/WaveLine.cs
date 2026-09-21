using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WaveLine : Entity
{
    private WaveLine()
    {
    }

    public WaveLine(
        int waveId,
        int salesOrderId,
        int salesOrderLineId,
        int lineNumber,
        int itemId,
        string itemSkuSnapshot,
        decimal requestedQuantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedQuantity);
        WaveId = waveId;
        SalesOrderId = salesOrderId;
        SalesOrderLineId = salesOrderLineId;
        LineNumber = lineNumber;
        ItemId = itemId;
        ItemSkuSnapshot = Required(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        RequestedQuantity = requestedQuantity;
        Status = WaveLineStatus.Selected;
        Revision = 1;
    }

    public int WaveId { get; private set; }
    public int SalesOrderId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int LineNumber { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public decimal RequestedQuantity { get; private set; }
    public decimal AllocatedQuantity { get; private set; }
    public decimal BackorderQuantity { get; private set; }
    public int? ReservationId { get; private set; }
    public int WorkCount { get; private set; }
    public WaveLineStatus Status { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Explanation { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Wave Wave { get; private set; } = null!;
    public SalesOrder SalesOrder { get; private set; } = null!;
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;

    public void StartProcessing(DateTime processedAtUtc)
    {
        if (Status is WaveLineStatus.Removed or WaveLineStatus.Cancelled or WaveLineStatus.Released)
        {
            return;
        }

        Status = WaveLineStatus.Processing;
        ProcessedAtUtc = NormalizeUtc(processedAtUtc);
        Touch(processedAtUtc);
    }

    public void RecordResult(
        decimal allocatedQuantity,
        decimal backorderQuantity,
        int? reservationId,
        int workCount,
        string explanation,
        DateTime processedAtUtc)
    {
        if (allocatedQuantity < 0m || backorderQuantity < 0m || workCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(allocatedQuantity));
        }

        AllocatedQuantity = allocatedQuantity;
        BackorderQuantity = backorderQuantity;
        ReservationId = reservationId;
        WorkCount = workCount;
        Explanation = Optional(explanation, 2_000);
        ErrorCode = null;
        ErrorMessage = null;
        Status = backorderQuantity > 0m
            ? WaveLineStatus.Shortage
            : workCount > 0
                ? WaveLineStatus.Released
                : WaveLineStatus.Allocated;
        ProcessedAtUtc = NormalizeUtc(processedAtUtc);
        Touch(processedAtUtc);
    }

    public void RecordFailure(string errorCode, string errorMessage, DateTime processedAtUtc)
    {
        ErrorCode = Required(errorCode, 120, nameof(errorCode));
        ErrorMessage = Required(errorMessage, 2_000, nameof(errorMessage));
        Status = WaveLineStatus.Failed;
        ProcessedAtUtc = NormalizeUtc(processedAtUtc);
        Touch(processedAtUtc);
    }

    public void MarkRemoved(string? reason = null)
    {
        if (Status == WaveLineStatus.Released)
        {
            throw new InvalidOperationException("Released wave lines cannot be removed.");
        }

        Status = WaveLineStatus.Removed;
        ErrorCode = "wave.line_removed";
        ErrorMessage = Optional(reason, 2_000);
        Touch();
    }

    public void MarkCancelled(string reason)
    {
        if (Status == WaveLineStatus.Released)
        {
            return;
        }

        Status = WaveLineStatus.Cancelled;
        ErrorCode = "wave.cancelled";
        ErrorMessage = Optional(reason, 2_000);
        Touch();
    }

    private void Touch(DateTime? timestampUtc = null)
    {
        Revision++;
        SetUpdatedAt(timestampUtc);
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, nameof(value));

}
