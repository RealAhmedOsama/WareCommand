using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class TransferOrder : Entity
{
    private readonly List<TransferOrderLine> _lines = [];

    private TransferOrder()
    {
    }

    public TransferOrder(
        string transferNumber,
        string creationIdempotencyKey,
        string creationRequestHash,
        int sourceWarehouseId,
        int destinationWarehouseId,
        int transitLocationId,
        string createdByUserId,
        DateTime createdAtUtc,
        int priority = 50,
        string? externalReference = null,
        string? notes = null)
    {
        if (sourceWarehouseId <= 0 || destinationWarehouseId <= 0 || sourceWarehouseId == destinationWarehouseId)
        {
            throw new ArgumentException("A transfer must have distinct source and destination warehouses.");
        }

        TransferNumber = Required(transferNumber, 80, nameof(transferNumber));
        CreationIdempotencyKey = Required(creationIdempotencyKey, 250, nameof(creationIdempotencyKey));
        CreationRequestHash = Required(creationRequestHash, 64, nameof(creationRequestHash));
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        TransitLocationId = Positive(transitLocationId, nameof(transitLocationId));
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        Priority = priority >= 0 ? priority : throw new ArgumentOutOfRangeException(nameof(priority));
        ExternalReference = Optional(externalReference, 100);
        Notes = Optional(notes, 2_000);
        Status = TransferOrderStatus.Draft;
        Revision = 1;
    }

    public string TransferNumber { get; private set; } = string.Empty;
    public string CreationIdempotencyKey { get; private set; } = string.Empty;
    public string CreationRequestHash { get; private set; } = string.Empty;
    public int SourceWarehouseId { get; private set; }
    public int DestinationWarehouseId { get; private set; }
    public int TransitLocationId { get; private set; }
    public int Priority { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? Notes { get; private set; }
    public TransferOrderStatus Status { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? ExceptionReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse SourceWarehouse { get; private set; } = null!;
    public Warehouse DestinationWarehouse { get; private set; } = null!;
    public Location TransitLocation { get; private set; } = null!;
    public IReadOnlyList<TransferOrderLine> Lines => _lines.AsReadOnly();

    public decimal RequestedQuantity => _lines.Sum(line => line.RequestedQuantity);
    public decimal ShippedQuantity => _lines.Sum(line => line.ShippedQuantity);
    public decimal ReceivedQuantity => _lines.Sum(line => line.ReceivedQuantity);

    public void AddLine(TransferOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != TransferOrderStatus.Draft)
        {
            throw new InvalidOperationException("Transfer lines can only be added while the transfer is draft.");
        }

        if (_lines.Any(existing => existing.Sequence == line.Sequence))
        {
            throw new InvalidOperationException("Transfer line sequence values must be unique.");
        }

        _lines.Add(line);
        Touch();
    }

    public void Confirm(DateTime confirmedAtUtc)
    {
        EnsureStatus(TransferOrderStatus.Draft);
        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A transfer must contain at least one line.");
        }

        Status = TransferOrderStatus.Confirmed;
        ConfirmedAtUtc = NormalizeUtc(confirmedAtUtc);
        Touch(confirmedAtUtc);
    }

    public void Release(DateTime releasedAtUtc)
    {
        if (Status is not (TransferOrderStatus.Confirmed or TransferOrderStatus.Allocated))
        {
            throw new InvalidOperationException($"A transfer in {Status} cannot be released.");
        }

        Status = TransferOrderStatus.Released;
        ReleasedAtUtc = NormalizeUtc(releasedAtUtc);
        Touch(releasedAtUtc);
    }

    public void RecordShipment(TransferOrderLine line, decimal quantity, DateTime shippedAtUtc)
    {
        if (Status is not (TransferOrderStatus.Released or TransferOrderStatus.Allocated or TransferOrderStatus.Picking))
        {
            throw new InvalidOperationException($"A transfer in {Status} cannot ship goods.");
        }

        line.RecordShipped(quantity);
        Status = _lines.All(value => value.IsFullyShipped)
            ? TransferOrderStatus.InTransit
            : TransferOrderStatus.Picking;
        if (Status == TransferOrderStatus.InTransit)
        {
            ShippedAtUtc ??= NormalizeUtc(shippedAtUtc);
        }

        Touch(shippedAtUtc);
    }

    public void RecordReceipt(TransferOrderLine line, decimal quantity, DateTime receivedAtUtc)
    {
        if (Status is not (TransferOrderStatus.InTransit or TransferOrderStatus.PartiallyReceived))
        {
            throw new InvalidOperationException($"A transfer in {Status} cannot receive goods.");
        }

        line.RecordReceived(quantity);
        Status = _lines.All(value => value.IsFullyReceived || value.Status == TransferLineStatus.Cancelled)
            ? TransferOrderStatus.Received
            : TransferOrderStatus.PartiallyReceived;
        if (Status == TransferOrderStatus.Received)
        {
            ReceivedAtUtc ??= NormalizeUtc(receivedAtUtc);
        }

        Touch(receivedAtUtc);
    }

    public void Close(DateTime closedAtUtc)
    {
        EnsureStatus(TransferOrderStatus.Received);
        Status = TransferOrderStatus.Closed;
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        Touch(closedAtUtc);
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (Status is TransferOrderStatus.Shipped or TransferOrderStatus.InTransit or
            TransferOrderStatus.PartiallyReceived or TransferOrderStatus.Received or
            TransferOrderStatus.Closed or TransferOrderStatus.Cancelled)
        {
            throw new InvalidOperationException($"A transfer in {Status} cannot be cancelled without a reversal.");
        }

        CancelledByUserId = Required(userId, 450, nameof(userId));
        CancellationReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Status = TransferOrderStatus.Cancelled;
        Touch(cancelledAtUtc);
    }

    public void MarkException(string reason, DateTime occurredAtUtc)
    {
        if (Status is TransferOrderStatus.Closed or TransferOrderStatus.Cancelled)
        {
            throw new InvalidOperationException("A closed or cancelled transfer cannot be changed to exception.");
        }

        ExceptionReason = Required(reason, 1_000, nameof(reason));
        Status = TransferOrderStatus.Exception;
        Touch(occurredAtUtc);
    }

    private void EnsureStatus(TransferOrderStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"A transfer in {Status} cannot transition to {expected}.");
        }
    }

    private void Touch(DateTime? timestampUtc = null)
    {
        Revision++;
        SetUpdatedAt(timestampUtc);
    }

    private static int Positive(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        return value;
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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
}

public sealed class TransferOrderLine : Entity
{
    private TransferOrderLine()
    {
    }

    public TransferOrderLine(
        int sequence,
        int itemId,
        decimal requestedQuantity,
        string baseUnitOfMeasure,
        int sourceLocationId,
        int destinationLocationId,
        int? lotId = null,
        int? serialNumberId = null,
        string? serialNumber = null,
        int? licensePlateId = null,
        int sourceInventoryStatusId = InventoryStatusSystemIds.Available,
        int destinationInventoryStatusId = InventoryStatusSystemIds.Available,
        string? notes = null,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        if (sequence <= 0 || itemId <= 0 || requestedQuantity <= 0 || sourceLocationId <= 0 || destinationLocationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (serialNumberId.HasValue && requestedQuantity != 1m)
        {
            throw new InvalidOperationException("A serialized transfer line must request exactly one unit.");
        }

        Sequence = sequence;
        ItemId = itemId;
        RequestedQuantity = requestedQuantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        LotId = PositiveOptional(lotId, nameof(lotId));
        SerialNumberId = PositiveOptional(serialNumberId, nameof(serialNumberId));
        SerialNumber = Optional(serialNumber, 100);
        LicensePlateId = PositiveOptional(licensePlateId, nameof(licensePlateId));
        SourceInventoryStatusId = Positive(sourceInventoryStatusId, nameof(sourceInventoryStatusId));
        DestinationInventoryStatusId = Positive(destinationInventoryStatusId, nameof(destinationInventoryStatusId));
        Notes = Optional(notes, 1_000);
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        Status = TransferLineStatus.Open;
        Revision = 1;
    }

    public int TransferOrderId { get; private set; }
    public int Sequence { get; private set; }
    public int ItemId { get; private set; }
    public decimal RequestedQuantity { get; private set; }
    public decimal ShippedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int SourceLocationId { get; private set; }
    public int DestinationLocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int SourceInventoryStatusId { get; private set; }
    public int DestinationInventoryStatusId { get; private set; }
    public string? Notes { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public TransferLineStatus Status { get; private set; }
    public long Revision { get; private set; }

    public TransferOrder TransferOrder { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public Location DestinationLocation { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public InventoryOwner? InventoryOwner { get; private set; }
    public bool IsFullyShipped => ShippedQuantity == RequestedQuantity;
    public bool IsFullyReceived => ReceivedQuantity == ShippedQuantity && IsFullyShipped;
    public decimal RemainingToShip => RequestedQuantity - ShippedQuantity;
    public decimal RemainingToReceive => ShippedQuantity - ReceivedQuantity;

    public void RecordShipped(decimal quantity)
    {
        ValidatePositive(quantity);
        if (quantity > RemainingToShip)
        {
            throw new InvalidOperationException("A transfer cannot ship more than its requested quantity.");
        }

        ShippedQuantity += quantity;
        Status = IsFullyShipped ? TransferLineStatus.Shipped : TransferLineStatus.PartiallyShipped;
        Touch();
    }

    public void RecordReceived(decimal quantity)
    {
        ValidatePositive(quantity);
        if (quantity > RemainingToReceive)
        {
            throw new InvalidOperationException("A transfer cannot receive more than its shipped quantity.");
        }

        ReceivedQuantity += quantity;
        Status = IsFullyReceived ? TransferLineStatus.Received : TransferLineStatus.PartiallyReceived;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidatePositive(decimal value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(value));
    }

    private static int? PositiveOptional(int? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value, parameterName);
        return value;
    }

    private static int Positive(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
        return value;
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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
}
