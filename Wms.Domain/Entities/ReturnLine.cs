using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReturnLine : Entity
{
    private ReturnLine()
    {
    }

    public ReturnLine(
        int returnAuthorizationId,
        int itemId,
        decimal expectedQuantity,
        int? salesOrderLineId,
        int? shipmentLineId,
        int? expectedLotId,
        int? expectedSerialNumberId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnAuthorizationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expectedQuantity, 0m);
        ReturnAuthorizationId = returnAuthorizationId;
        ItemId = itemId;
        ExpectedQuantity = expectedQuantity;
        SalesOrderLineId = salesOrderLineId;
        ShipmentLineId = shipmentLineId;
        ExpectedLotId = expectedLotId;
        ExpectedSerialNumberId = expectedSerialNumberId;
        Revision = 1;
    }

    public int ReturnAuthorizationId { get; private set; }
    public int ItemId { get; private set; }
    public int? SalesOrderLineId { get; private set; }
    public int? ShipmentLineId { get; private set; }
    public int? ExpectedLotId { get; private set; }
    public int? ExpectedSerialNumberId { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal ReceivedQuantity { get; private set; }
    public decimal DisposedQuantity { get; private set; }
    public long Revision { get; private set; }

    public ReturnAuthorization ReturnAuthorization { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public SalesOrderLine? SalesOrderLine { get; private set; }
    public ShipmentLine? ShipmentLine { get; private set; }
    public decimal RemainingToReceive => Math.Max(0m, ExpectedQuantity - ReceivedQuantity);
    public decimal RemainingToDispose => Math.Max(0m, ReceivedQuantity - DisposedQuantity);

    public void RecordReceipt(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        if (quantity > RemainingToReceive)
        {
            throw new InvalidOperationException("The return receipt exceeds the authorized quantity.");
        }

        ReceivedQuantity += quantity;
        Touch();
    }

    public void RecordDisposition(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        if (quantity > RemainingToDispose)
        {
            throw new InvalidOperationException("The return disposition exceeds the received quantity.");
        }

        DisposedQuantity += quantity;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }
}
