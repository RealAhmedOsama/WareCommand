// Wms.Domain/Entities/Movement.cs

using Wms.Domain.Common;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Entities;

public class Movement : Entity
{
    // EF Constructor
    private Movement()
    {
    }

    public Movement(MovementType type, int itemId, Quantity quantity, string userId,
        int? fromLocationId = null, int? toLocationId = null, int? lotId = null,
        string? serialNumber = null, string? referenceNumber = null, string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required", nameof(userId));

        Type = type;
        ItemId = itemId;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        LotId = lotId;
        SerialNumber = serialNumber?.Trim();
        Quantity = quantity;
        UserId = userId.Trim();
        ReferenceNumber = referenceNumber?.Trim();
        Notes = notes?.Trim();
    }

    public MovementType Type { get; private set; }
    public int ItemId { get; private set; }
    public int? FromLocationId { get; private set; }
    public int? ToLocationId { get; private set; }
    public int? LotId { get; private set; }
    public string? SerialNumber { get; private set; }
    public Quantity Quantity { get; private set; } = Quantity.Zero;
    public string UserId { get; private set; } = string.Empty;
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;

    // Navigation properties
    public Item Item { get; private set; } = null!;
    public Location? FromLocation { get; private set; }
    public Location? ToLocation { get; private set; }
    public Lot? Lot { get; private set; }

    public static Movement CreateReceipt(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null)
    {
        return new Movement(MovementType.Receipt, itemId, quantity, userId,
            toLocationId: locationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes);
    }

    public static Movement CreatePutaway(int itemId, int fromLocationId, int toLocationId,
        Quantity quantity, string userId, int? lotId = null, string? serialNumber = null,
        string? referenceNumber = null, string? notes = null)
    {
        return new Movement(MovementType.Putaway, itemId, quantity, userId,
            fromLocationId, toLocationId, lotId,
            serialNumber, referenceNumber, notes);
    }

    public static Movement CreatePick(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null)
    {
        return new Movement(MovementType.Pick, itemId, quantity, userId,
            fromLocationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes);
    }

    public static Movement CreateAdjustment(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null)
    {
        return new Movement(MovementType.Adjustment, itemId, quantity, userId,
            toLocationId: locationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes);
    }
}
