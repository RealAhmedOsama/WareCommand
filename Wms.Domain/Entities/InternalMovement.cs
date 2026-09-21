using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class InternalMovement : Entity
{
    private InternalMovement()
    {
    }

    public InternalMovement(
        string idempotencyKey,
        string requestHash,
        int warehouseId,
        int itemId,
        decimal quantity,
        string baseUnitOfMeasure,
        int sourceLocationId,
        int destinationLocationId,
        string createdByUserId,
        DateTime createdAtUtc,
        int? lotId = null,
        int? serialNumberId = null,
        string? serialNumber = null,
        int? licensePlateId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        string? reason = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        if (warehouseId <= 0 || itemId <= 0 || quantity <= 0 || sourceLocationId <= 0 || destinationLocationId <= 0 || sourceLocationId == destinationLocationId)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId));
        }

        if (serialNumberId.HasValue && quantity != 1m)
        {
            throw new InvalidOperationException("A serialized internal movement must move exactly one unit.");
        }

        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        RequestHash = Required(requestHash, 64, nameof(requestHash));
        WarehouseId = warehouseId;
        ItemId = itemId;
        Quantity = quantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        LotId = PositiveOptional(lotId, nameof(lotId));
        SerialNumberId = PositiveOptional(serialNumberId, nameof(serialNumberId));
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = PositiveOptional(licensePlateId, nameof(licensePlateId));
        InventoryStatusId = Positive(inventoryStatusId, nameof(inventoryStatusId));
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Reason = Optional(reason, 1_000);
        Status = InternalMovementStatus.Requested;
        Revision = 1;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int SourceLocationId { get; private set; }
    public int DestinationLocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public InternalMovementStatus Status { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public Location DestinationLocation { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryOwner? InventoryOwner { get; private set; }

    public void Complete(DateTime completedAtUtc)
    {
        if (Status != InternalMovementStatus.Requested)
        {
            throw new InvalidOperationException($"An internal movement in {Status} cannot be completed.");
        }

        Status = InternalMovementStatus.Completed;
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Revision++;
        SetUpdatedAt(completedAtUtc);
    }

    public void Cancel(string reason, DateTime cancelledAtUtc)
    {
        if (Status != InternalMovementStatus.Requested)
        {
            throw new InvalidOperationException($"An internal movement in {Status} cannot be cancelled.");
        }

        Status = InternalMovementStatus.Cancelled;
        CancellationReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Revision++;
        SetUpdatedAt(cancelledAtUtc);
    }

    private static int? PositiveOptional(int? value, string parameterName) =>
        value is null ? null : value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static int Positive(int value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

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
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
}
