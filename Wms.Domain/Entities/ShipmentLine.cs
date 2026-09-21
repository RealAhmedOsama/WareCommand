using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class ShipmentLine : Entity
{
    private ShipmentLine()
    {
    }

    public ShipmentLine(
        int shipmentId,
        int salesOrderLineId,
        int itemId,
        decimal quantity,
        string baseUnitOfMeasure)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(salesOrderLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);

        ShipmentId = shipmentId;
        SalesOrderLineId = salesOrderLineId;
        ItemId = itemId;
        Quantity = quantity;
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        Revision = 1;
    }

    public int ShipmentId { get; private set; }
    public int SalesOrderLineId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public long Revision { get; private set; }

    public Shipment Shipment { get; private set; } = null!;
    public SalesOrderLine SalesOrderLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;

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
