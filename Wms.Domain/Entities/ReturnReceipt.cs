using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReturnReceipt : Entity
{
    private ReturnReceipt()
    {
    }

    public ReturnReceipt(
        int returnAuthorizationId,
        int returnLineId,
        int itemId,
        decimal quantity,
        int returnLocationId,
        int inventoryStatusId,
        int? lotId,
        int? serialNumberId,
        string? serialNumber,
        int? licensePlateId,
        DateTime receivedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnAuthorizationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnLineId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        ReturnAuthorizationId = returnAuthorizationId;
        ReturnLineId = returnLineId;
        ItemId = itemId;
        Quantity = quantity;
        ReturnLocationId = returnLocationId;
        InventoryStatusId = inventoryStatusId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber.Trim();
        LicensePlateId = licensePlateId;
        ReceivedAtUtc = DateTime.SpecifyKind(receivedAtUtc, DateTimeKind.Utc);
        Status = ReturnReceiptStatus.PendingDisposition;
        Revision = 1;
    }

    public int ReturnAuthorizationId { get; private set; }
    public int ReturnLineId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal DisposedQuantity { get; private set; }
    public int ReturnLocationId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public ReturnReceiptStatus Status { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public ReturnAuthorization ReturnAuthorization { get; private set; } = null!;
    public ReturnLine ReturnLine { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public LicensePlate? LicensePlate { get; private set; }
    public decimal RemainingToDispose => Math.Max(0m, Quantity - DisposedQuantity);

    public void RecordDisposition(decimal quantity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        if (quantity > RemainingToDispose)
        {
            throw new InvalidOperationException("The disposition exceeds the received return quantity.");
        }

        DisposedQuantity += quantity;
        Status = RemainingToDispose == 0m
            ? ReturnReceiptStatus.Disposed
            : ReturnReceiptStatus.PartiallyDisposed;
        Revision++;
        SetUpdatedAt();
    }
}
