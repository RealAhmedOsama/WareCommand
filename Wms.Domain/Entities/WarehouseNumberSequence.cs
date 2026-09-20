using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class WarehouseNumberSequence : Entity
{
    private WarehouseNumberSequence()
    {
    }

    public WarehouseNumberSequence(int warehouseId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId, nameof(warehouseId));

        WarehouseId = warehouseId;
    }

    public int WarehouseId { get; private set; }
    public long NextReceiptNumber { get; private set; } = 1;
    public long NextOrderNumber { get; private set; } = 1;
    public long NextAdvanceShippingNoticeNumber { get; private set; } = 1;
    public long NextWorkNumber { get; private set; } = 1;
    public long NextShipmentNumber { get; private set; } = 1;
    public long NextTransferNumber { get; private set; } = 1;
    public long NextCountNumber { get; private set; } = 1;
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;

    public void SetNextNumbers(
        long receipt,
        long order,
        long work,
        long shipment,
        long transfer,
        long count)
    {
        Validate(receipt, nameof(receipt));
        Validate(order, nameof(order));
        Validate(work, nameof(work));
        Validate(shipment, nameof(shipment));
        Validate(transfer, nameof(transfer));
        Validate(count, nameof(count));

        NextReceiptNumber = receipt;
        NextOrderNumber = order;
        NextWorkNumber = work;
        NextShipmentNumber = shipment;
        NextTransferNumber = transfer;
        NextCountNumber = count;
        Revision++;
        SetUpdatedAt();
    }

    public long AllocateOrderNumber()
    {
        var allocated = NextOrderNumber;
        if (allocated < 1 || allocated == long.MaxValue)
        {
            throw new InvalidOperationException("The warehouse order number sequence is exhausted.");
        }

        NextOrderNumber++;
        Revision++;
        SetUpdatedAt();
        return allocated;
    }

    public long AllocateAdvanceShippingNoticeNumber()
    {
        var allocated = NextAdvanceShippingNoticeNumber;
        if (allocated < 1 || allocated == long.MaxValue)
        {
            throw new InvalidOperationException("The warehouse ASN number sequence is exhausted.");
        }

        NextAdvanceShippingNoticeNumber++;
        Revision++;
        SetUpdatedAt();
        return allocated;
    }

    public long AllocateReceiptNumber()
    {
        var allocated = NextReceiptNumber;
        if (allocated < 1 || allocated == long.MaxValue)
        {
            throw new InvalidOperationException("The warehouse receipt number sequence is exhausted.");
        }

        NextReceiptNumber++;
        Revision++;
        SetUpdatedAt();
        return allocated;
    }

    private static void Validate(long value, string name)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(name, "Sequence numbers must be positive.");
    }
}
