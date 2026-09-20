using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

public sealed class LicensePlateContent : Entity
{
    private LicensePlateContent()
    {
    }

    public LicensePlateContent(
        int licensePlateId,
        int itemId,
        Quantity quantity,
        int? lotId = null,
        int? serialNumberId = null,
        int inventoryStatusId = InventoryStatusSystemIds.Available,
        int? itemPackagingId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(licensePlateId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(inventoryStatusId);
        if (lotId is <= 0 || serialNumberId is <= 0 || itemPackagingId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lotId));
        }

        ValidateQuantity(quantity, serialNumberId);
        LicensePlateId = licensePlateId;
        ItemId = itemId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        InventoryStatusId = inventoryStatusId;
        ItemPackagingId = itemPackagingId;
        Quantity = quantity;
    }

    public int LicensePlateId { get; private set; }
    public int ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public int InventoryStatusId { get; private set; }
    public int? ItemPackagingId { get; private set; }
    public Quantity Quantity { get; private set; } = Quantity.Zero;

    public LicensePlate LicensePlate { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? SerialNumber { get; private set; }
    public InventoryStatus InventoryStatus { get; private set; } = null!;
    public ItemPackaging? ItemPackaging { get; private set; }

    public void AddQuantity(Quantity quantity)
    {
        ValidateQuantity(quantity, SerialNumberId);
        Quantity = new Quantity(Quantity.Value + quantity.Value);
        ValidateQuantity(Quantity, SerialNumberId);
        SetUpdatedAt();
    }

    public void RemoveQuantity(Quantity quantity)
    {
        ValidateQuantity(quantity, SerialNumberId);
        var remaining = Quantity.Value - quantity.Value;
        if (remaining < 0)
        {
            throw new InvalidOperationException("Cannot remove more content than the license plate contains.");
        }

        if (remaining > 0 && SerialNumberId.HasValue)
        {
            throw new InvalidOperationException("Serial-controlled content must move as one whole unit.");
        }

        Quantity = new Quantity(remaining);
        SetUpdatedAt();
    }

    public void SetQuantity(Quantity quantity)
    {
        ValidateQuantity(quantity, SerialNumberId);
        Quantity = quantity;
        SetUpdatedAt();
    }

    private static void ValidateQuantity(Quantity quantity, int? serialNumberId)
    {
        if (quantity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "License plate content quantity must be positive.");
        }

        if (serialNumberId.HasValue && quantity.Value != 1m)
        {
            throw new InvalidOperationException("Serial-controlled content must have a quantity of exactly one.");
        }
    }
}
