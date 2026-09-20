// Wms.Domain/Entities/Stock.cs

using Wms.Domain.Common;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

public class Stock : Entity
{
    // EF Constructor
    private Stock()
    {
    }

    public Stock(
        int itemId,
        int locationId,
        Quantity quantity,
        int? lotId = null,
        string? serialNumber = null,
        int? serialNumberId = null)
    {
        ItemId = itemId;
        LocationId = locationId;
        LotId = lotId;
        SerialNumber = serialNumber?.Trim();
        SerialNumberId = serialNumberId;
        QuantityAvailable = quantity;
        QuantityReserved = Quantity.Zero;
    }

    public int ItemId { get; private set; }
    public int LocationId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public string? SerialNumber { get; private set; }
    public Quantity QuantityAvailable { get; private set; } = Quantity.Zero;
    public Quantity QuantityReserved { get; private set; } = Quantity.Zero;

    // Navigation properties
    public Item Item { get; private set; } = null!;
    public Location Location { get; private set; } = null!;
    public Lot? Lot { get; private set; }
    public SerialNumber? Serial { get; private set; }

    public void AddQuantity(Quantity quantity)
    {
        if (quantity.Value <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        QuantityAvailable = new Quantity(QuantityAvailable.Value + quantity.Value);
        SetUpdatedAt();
    }

    public void RemoveQuantity(Quantity quantity)
    {
        if (quantity.Value <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        var newQuantity = QuantityAvailable.Value - quantity.Value;
        if (newQuantity < 0)
            throw new InvalidOperationException("Cannot remove more quantity than available");

        QuantityAvailable = new Quantity(newQuantity);
        SetUpdatedAt();
    }

    public void ReserveQuantity(Quantity quantity)
    {
        if (quantity.Value <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        var availableToReserve = QuantityAvailable.Value - QuantityReserved.Value;
        if (quantity.Value > availableToReserve)
            throw new InvalidOperationException("Cannot reserve more quantity than available");

        QuantityReserved = new Quantity(QuantityReserved.Value + quantity.Value);
        SetUpdatedAt();
    }

    public void ReleaseReservation(Quantity quantity)
    {
        if (quantity.Value <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));

        var newReserved = QuantityReserved.Value - quantity.Value;
        if (newReserved < 0)
            throw new InvalidOperationException("Cannot release more than reserved");

        QuantityReserved = new Quantity(newReserved);
        SetUpdatedAt();
    }

    public Quantity GetAvailableQuantity()
    {
        return new Quantity(QuantityAvailable.Value - QuantityReserved.Value);
    }

    public void AdjustQuantity(Quantity newQuantity, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required for quantity adjustment", nameof(reason));

        QuantityAvailable = newQuantity;
        SetUpdatedAt();
    }
}
