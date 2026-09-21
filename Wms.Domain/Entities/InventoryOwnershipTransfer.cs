using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class InventoryOwnershipTransfer : Entity
{
    private InventoryOwnershipTransfer()
    {
    }

    public InventoryOwnershipTransfer(
        string transferNumber,
        string idempotencyKey,
        string requestHash,
        int warehouseId,
        int itemId,
        decimal quantity,
        string baseUnitOfMeasure,
        int locationId,
        int inventoryStatusId,
        InventoryOwnerKind sourceOwnerKind,
        int? sourceInventoryOwnerId,
        string sourceOwnerCodeSnapshot,
        InventoryOwnerKind destinationOwnerKind,
        int? destinationInventoryOwnerId,
        string destinationOwnerCodeSnapshot,
        string createdByUserId,
        DateTime createdAtUtc,
        int? lotId = null,
        int? serialNumberId = null,
        string? serialNumber = null,
        int? licensePlateId = null,
        string? reason = null)
    {
        if (warehouseId <= 0 || itemId <= 0 || quantity <= 0m || locationId <= 0 || inventoryStatusId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId));
        }

        if (sourceOwnerKind == destinationOwnerKind && sourceInventoryOwnerId == destinationInventoryOwnerId)
        {
            throw new InvalidOperationException("An ownership transfer must change the inventory owner.");
        }

        TransferNumber = Required(transferNumber, 80, nameof(transferNumber));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        RequestHash = Required(requestHash, 64, nameof(requestHash));
        WarehouseId = warehouseId;
        ItemId = itemId;
        Quantity = quantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        LocationId = locationId;
        InventoryStatusId = inventoryStatusId;
        LotId = PositiveOptional(lotId, nameof(lotId));
        SerialNumberId = PositiveOptional(serialNumberId, nameof(serialNumberId));
        SerialNumber = OptionalUpper(serialNumber, 100);
        LicensePlateId = PositiveOptional(licensePlateId, nameof(licensePlateId));
        SourceOwnerKind = sourceOwnerKind;
        SourceInventoryOwnerId = PositiveOptional(sourceInventoryOwnerId, nameof(sourceInventoryOwnerId));
        SourceOwnerCodeSnapshot = Required(sourceOwnerCodeSnapshot, 80, nameof(sourceOwnerCodeSnapshot)).ToUpperInvariant();
        DestinationOwnerKind = destinationOwnerKind;
        DestinationInventoryOwnerId = PositiveOptional(destinationInventoryOwnerId, nameof(destinationInventoryOwnerId));
        DestinationOwnerCodeSnapshot = Required(destinationOwnerCodeSnapshot, 80, nameof(destinationOwnerCodeSnapshot)).ToUpperInvariant();
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        CreatedAtUtc = NormalizeUtc(createdAtUtc);
        Reason = Optional(reason, 1_000);
        Status = InventoryOwnershipTransferStatus.Approved;
        Revision = 1;
    }

    public string TransferNumber { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int LocationId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public InventoryOwnerKind SourceOwnerKind { get; private set; }
    public int? SourceInventoryOwnerId { get; private set; }
    public string SourceOwnerCodeSnapshot { get; private set; } = string.Empty;
    public InventoryOwnerKind DestinationOwnerKind { get; private set; }
    public int? DestinationInventoryOwnerId { get; private set; }
    public string DestinationOwnerCodeSnapshot { get; private set; } = string.Empty;
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? Reason { get; private set; }
    public InventoryOwnershipTransferStatus Status { get; private set; }
    public string? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ExceptionReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public InventoryOwner? SourceOwner { get; private set; }
    public InventoryOwner? DestinationOwner { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }

    public void Complete(string userId, DateTime completedAtUtc)
    {
        if (Status != InventoryOwnershipTransferStatus.Approved)
        {
            throw new InvalidOperationException($"An ownership transfer in {Status} cannot be completed.");
        }

        CompletedByUserId = Required(userId, 450, nameof(userId));
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Status = InventoryOwnershipTransferStatus.Completed;
        Revision++;
        SetUpdatedAt(completedAtUtc);
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (Status == InventoryOwnershipTransferStatus.Completed)
        {
            throw new InvalidOperationException("A completed ownership transfer requires a compensating transfer.");
        }

        CancelledByUserId = Required(userId, 450, nameof(userId));
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Status = InventoryOwnershipTransferStatus.Cancelled;
        Revision++;
        SetUpdatedAt(cancelledAtUtc);
    }

    public void MarkException(string reason, DateTime occurredAtUtc)
    {
        if (Status is InventoryOwnershipTransferStatus.Completed or InventoryOwnershipTransferStatus.Cancelled)
        {
            throw new InvalidOperationException($"An ownership transfer in {Status} cannot be moved to exception.");
        }

        ExceptionReason = Required(reason, 1_000, nameof(reason));
        Status = InventoryOwnershipTransferStatus.Exception;
        Revision++;
        SetUpdatedAt(occurredAtUtc);
    }

    private static int? PositiveOptional(int? value, string parameterName) =>
        value is null ? null : value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
