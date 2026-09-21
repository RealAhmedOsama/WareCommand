using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// A durable recall selector. Trace rows are derived from the immutable ledger
/// and current balances, so closing a case never deletes inventory history.
/// </summary>
public sealed class InventoryRecallCase : Entity
{
    private InventoryRecallCase()
    {
    }

    public InventoryRecallCase(
        string caseNumber,
        string idempotencyKey,
        int warehouseId,
        int? itemId,
        int? lotId,
        int? serialNumberId,
        int? licensePlateId,
        string reason,
        string createdByUserId,
        string? externalReference,
        DateTime createdAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!itemId.HasValue && !lotId.HasValue && !serialNumberId.HasValue && !licensePlateId.HasValue)
        {
            throw new ArgumentException(
                "At least one item, lot, serial, or license plate selector is required.",
                nameof(itemId));
        }

        CaseNumber = Required(caseNumber, 80, nameof(caseNumber));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        WarehouseId = warehouseId;
        ItemId = itemId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        LicensePlateId = licensePlateId;
        Reason = Required(reason, 1_000, nameof(reason));
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        ExternalReference = Optional(externalReference, 200);
        CreatedAtUtc = NormalizeUtc(createdAtUtc);
        Status = InventoryRecallStatus.Open;
        Revision = 1;
    }

    public string CaseNumber { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int? ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public int? LicensePlateId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? ExternalReference { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public InventoryRecallStatus Status { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosureReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item? Item { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }

    public void MarkContained()
    {
        if (Status != InventoryRecallStatus.Open)
        {
            throw new InvalidOperationException($"A recall case in {Status} cannot be contained.");
        }

        Status = InventoryRecallStatus.Contained;
        Revision++;
        SetUpdatedAt();
    }

    public void Close(string userId, string reason, DateTime closedAtUtc)
    {
        if (Status is InventoryRecallStatus.Closed or InventoryRecallStatus.Cancelled)
        {
            throw new InvalidOperationException($"A recall case in {Status} cannot be closed.");
        }

        ClosedByUserId = Required(userId, 450, nameof(userId));
        ClosureReason = Required(reason, 1_000, nameof(reason));
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        Status = InventoryRecallStatus.Closed;
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
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
