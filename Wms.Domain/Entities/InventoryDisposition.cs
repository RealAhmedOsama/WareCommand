using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// An auditable, idempotent request to move a precise stock quantity to a
/// non-saleable or controlled inventory status. It intentionally does not
/// delete stock or ledger history.
/// </summary>
public sealed class InventoryDisposition : Entity
{
    private InventoryDisposition()
    {
    }

    public InventoryDisposition(
        string dispositionNumber,
        string idempotencyKey,
        int warehouseId,
        int stockId,
        int itemId,
        int locationId,
        int? lotId,
        int? serialNumberId,
        int? licensePlateId,
        int sourceInventoryStatusId,
        int targetInventoryStatusId,
        string baseUnitOfMeasure,
        InventoryDispositionKind kind,
        decimal requestedQuantity,
        string reason,
        string requestedByUserId,
        string? referenceNumber,
        string? witnessUserId,
        bool approvalRequired,
        DateTime requestedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stockId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceInventoryStatusId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetInventoryStatusId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestedQuantity, 0m);

        DispositionNumber = Required(dispositionNumber, 80, nameof(dispositionNumber));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        WarehouseId = warehouseId;
        StockId = stockId;
        ItemId = itemId;
        LocationId = locationId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        LicensePlateId = licensePlateId;
        SourceInventoryStatusId = sourceInventoryStatusId;
        TargetInventoryStatusId = targetInventoryStatusId;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        Kind = kind;
        RequestedQuantity = requestedQuantity;
        Reason = Required(reason, 1_000, nameof(reason));
        RequestedByUserId = Required(requestedByUserId, 450, nameof(requestedByUserId));
        ReferenceNumber = Optional(referenceNumber, 100);
        WitnessUserId = Optional(witnessUserId, 450);
        ApprovalRequired = approvalRequired;
        RequestedAtUtc = NormalizeUtc(requestedAtUtc);
        Status = approvalRequired
            ? InventoryDispositionStatus.PendingApproval
            : InventoryDispositionStatus.Approved;
        if (!approvalRequired)
        {
            ApprovedByUserId = RequestedByUserId;
            ApprovedAtUtc = RequestedAtUtc;
        }

        Revision = 1;
    }

    public string DispositionNumber { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int StockId { get; private set; }
    public int ItemId { get; private set; }
    public int LocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int SourceInventoryStatusId { get; private set; }
    public int TargetInventoryStatusId { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public InventoryDispositionKind Kind { get; private set; }
    public InventoryDispositionStatus Status { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public decimal CompletedQuantity { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string RequestedByUserId { get; private set; } = string.Empty;
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? FailureReason { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? WitnessUserId { get; private set; }
    public bool ApprovalRequired { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public int? DestinationStockId { get; private set; }
    public int? OutboundMovementId { get; private set; }
    public int? InboundMovementId { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Stock Stock { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryStatus SourceInventoryStatus { get; private set; } = null!;
    public InventoryStatus TargetInventoryStatus { get; private set; } = null!;

    public void Approve(string userId, DateTime approvedAtUtc)
    {
        if (Status != InventoryDispositionStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"A disposition in {Status} cannot be approved.");
        }

        ApprovedByUserId = Required(userId, 450, nameof(userId));
        ApprovedAtUtc = NormalizeUtc(approvedAtUtc);
        Status = InventoryDispositionStatus.Approved;
        Revision++;
        SetUpdatedAt();
    }

    public void Reject(string userId, string reason, DateTime rejectedAtUtc)
    {
        if (Status != InventoryDispositionStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"A disposition in {Status} cannot be rejected.");
        }

        ApprovedByUserId = Required(userId, 450, nameof(userId));
        RejectionReason = Required(reason, 1_000, nameof(reason));
        CompletedAtUtc = NormalizeUtc(rejectedAtUtc);
        Status = InventoryDispositionStatus.Rejected;
        Revision++;
        SetUpdatedAt();
    }

    public void MarkExecuting()
    {
        if (Status != InventoryDispositionStatus.Approved)
        {
            throw new InvalidOperationException(
                $"A disposition in {Status} cannot start execution.");
        }

        Status = InventoryDispositionStatus.Executing;
        FailureReason = null;
        Revision++;
        SetUpdatedAt();
    }

    public void MarkCompleted(
        string userId,
        decimal completedQuantity,
        int? destinationStockId,
        int outboundMovementId,
        int inboundMovementId,
        DateTime completedAtUtc)
    {
        if (Status != InventoryDispositionStatus.Executing)
        {
            throw new InvalidOperationException(
                $"A disposition in {Status} cannot be completed.");
        }

        if (completedQuantity <= 0m || completedQuantity > RequestedQuantity)
        {
            throw new ArgumentOutOfRangeException(nameof(completedQuantity));
        }

        CompletedByUserId = Required(userId, 450, nameof(userId));
        CompletedQuantity = completedQuantity;
        DestinationStockId = destinationStockId;
        OutboundMovementId = outboundMovementId;
        InboundMovementId = inboundMovementId;
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Status = InventoryDispositionStatus.Completed;
        Revision++;
        SetUpdatedAt();
    }

    public void MarkException(string reason)
    {
        FailureReason = Required(reason, 1_000, nameof(reason));
        Status = InventoryDispositionStatus.Exception;
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
