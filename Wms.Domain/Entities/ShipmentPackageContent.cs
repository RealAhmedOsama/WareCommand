using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Entities;

public sealed class ShipmentPackageContent : Entity
{
    private ShipmentPackageContent()
    {
    }

    public ShipmentPackageContent(
        int shipmentPackageId,
        int salesOrderLineId,
        int itemId,
        decimal quantity,
        string baseUnitOfMeasure,
        int sourceLocationId,
        int? sourceLicensePlateId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int inventoryStatusId,
        decimal? expectedWeightKg,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentPackageId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        if (sourceLicensePlateId is <= 0 || lotId is <= 0 || serialNumberId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceLicensePlateId));
        }

        ShipmentPackageId = shipmentPackageId;
        SalesOrderLineId = salesOrderLineId;
        ItemId = itemId;
        Quantity = quantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        SourceLocationId = sourceLocationId;
        SourceLicensePlateId = sourceLicensePlateId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = Optional(serialNumber, 100);
        InventoryStatusId = inventoryStatusId;
        ExpectedWeightKg = expectedWeightKg;
        OwnerKind = ownerKind;
        InventoryOwnerId = inventoryOwnerId;
        OwnerCodeSnapshot = InventoryOwnershipDimension.NormalizeOwnerCode(
            ownerKind,
            inventoryOwnerId,
            ownerCodeSnapshot);
        Revision = 1;
    }

    public int ShipmentPackageId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public int SourceLocationId { get; private set; }
    public int? SourceLicensePlateId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int InventoryStatusId { get; private set; }
    public InventoryOwnerKind OwnerKind { get; private set; }
    public int? InventoryOwnerId { get; private set; }
    public string OwnerCodeSnapshot { get; private set; } = InventoryOwnershipDimension.CompanyOwnerCode;
    public decimal? ExpectedWeightKg { get; private set; }
    public long Revision { get; private set; }

    public ShipmentPackage ShipmentPackage { get; private set; } = null!;
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Location SourceLocation { get; private set; } = null!;
    public LicensePlate? SourceLicensePlate { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? SerialNumberEntity { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public InventoryOwner? InventoryOwner { get; private set; }

    public void AddQuantity(decimal quantity, decimal? expectedWeightKg = null)
    {
        ValidatePositive(quantity);
        if (SerialNumberId.HasValue && Quantity + quantity > 1m)
        {
            throw new InvalidOperationException("A serial-controlled package content cannot exceed one unit.");
        }

        Quantity += quantity;
        if (expectedWeightKg.HasValue)
        {
            ExpectedWeightKg = (ExpectedWeightKg ?? 0m) + expectedWeightKg.Value;
        }
        Touch();
    }

    public void RemoveQuantity(decimal quantity)
    {
        ValidatePositive(quantity);
        if (quantity > Quantity)
        {
            throw new InvalidOperationException("Package content cannot remove more than it contains.");
        }

        if (SerialNumberId.HasValue && quantity != Quantity)
        {
            throw new InvalidOperationException("Serial-controlled package content must be removed as one unit.");
        }

        Quantity -= quantity;
        Touch();
    }

    private void Touch()
    {
        Revision++;
    }

    private static void ValidatePositive(decimal quantity) =>
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

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
            : Required(value, maximumLength, nameof(value));
}
