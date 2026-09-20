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
        string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null)
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
        Timestamp = timestampUtc.HasValue
            ? NormalizeUtc(timestampUtc.Value)
            : DateTime.UnixEpoch;

        var conversion = quantity.ConversionSnapshot;
        EnteredQuantity = conversion?.EnteredQuantity ?? quantity.Value;
        EnteredUnitOfMeasure = conversion?.EnteredUnitOfMeasure ?? "BASE";
        BaseUnitOfMeasure = conversion?.BaseUnitOfMeasure ?? "BASE";
        ConversionFactorToBase = conversion?.ConversionFactorToBase ?? 1m;
        ConversionPrecision = conversion?.ResultPrecision ?? 4;
        ConversionRoundingMode = conversion?.RoundingMode ?? QuantityRoundingMode.Reject;
        ConversionRoundingDelta = conversion?.RoundingDelta ?? 0m;
        ConversionPath = conversion?.ConversionPath ?? "BASE";
        ConversionRuleIds = conversion?.ConversionRuleIds ?? string.Empty;
    }

    public MovementType Type { get; private set; }
    public int ItemId { get; private set; }
    public int? FromLocationId { get; private set; }
    public int? ToLocationId { get; private set; }
    public int? LotId { get; private set; }
    public string? SerialNumber { get; private set; }
    public Quantity Quantity { get; private set; } = Quantity.Zero;
    public decimal EnteredQuantity { get; private set; }
    public string EnteredUnitOfMeasure { get; private set; } = "BASE";
    public string BaseUnitOfMeasure { get; private set; } = "BASE";
    public decimal ConversionFactorToBase { get; private set; } = 1m;
    public int ConversionPrecision { get; private set; } = 4;
    public QuantityRoundingMode ConversionRoundingMode { get; private set; } = QuantityRoundingMode.Reject;
    public decimal ConversionRoundingDelta { get; private set; }
    public string ConversionPath { get; private set; } = "BASE";
    public string ConversionRuleIds { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public DateTime Timestamp { get; private set; } = DateTime.UnixEpoch;

    // Navigation properties
    public Item Item { get; private set; } = null!;
    public Location? FromLocation { get; private set; }
    public Location? ToLocation { get; private set; }
    public Lot? Lot { get; private set; }

    public static Movement CreateReceipt(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null)
    {
        return new Movement(MovementType.Receipt, itemId, quantity, userId,
            toLocationId: locationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes, timestampUtc: timestampUtc);
    }

    public static Movement CreatePutaway(int itemId, int fromLocationId, int toLocationId,
        Quantity quantity, string userId, int? lotId = null, string? serialNumber = null,
        string? referenceNumber = null, string? notes = null, DateTime? timestampUtc = null)
    {
        return new Movement(MovementType.Putaway, itemId, quantity, userId,
            fromLocationId, toLocationId, lotId,
            serialNumber, referenceNumber, notes, timestampUtc);
    }

    public static Movement CreatePick(int itemId, int fromLocationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null)
    {
        return new Movement(MovementType.Pick, itemId, quantity, userId,
            fromLocationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes, timestampUtc: timestampUtc);
    }

    public static Movement CreateAdjustment(int itemId, int locationId, Quantity quantity, string userId,
        int? lotId = null, string? serialNumber = null, string? referenceNumber = null, string? notes = null,
        DateTime? timestampUtc = null)
    {
        return new Movement(MovementType.Adjustment, itemId, quantity, userId,
            toLocationId: locationId, lotId: lotId, serialNumber: serialNumber,
            referenceNumber: referenceNumber, notes: notes, timestampUtc: timestampUtc);
    }
}
