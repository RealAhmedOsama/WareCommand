// Wms.Domain/Entities/Lot.cs

using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public class Lot : Entity
{
    // EF Constructor
    private Lot()
    {
    }

    public Lot(string number, int itemId, DateTime? expiryDate = null, DateTime? manufacturedDate = null)
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Lot number is required", nameof(number));

        var normalizedExpiryDate = NormalizeDateOnly(expiryDate);
        var normalizedManufacturedDate = NormalizeDateOnly(manufacturedDate);

        Number = number.Trim().ToUpperInvariant();
        ItemId = itemId;
        ExpiryDate = normalizedExpiryDate;
        ManufacturedDate = normalizedManufacturedDate;

        if (normalizedExpiryDate.HasValue && normalizedManufacturedDate.HasValue &&
            normalizedExpiryDate < normalizedManufacturedDate)
            throw new ArgumentException("Expiry date cannot be before manufactured date");
    }

    public string Number { get; private set; } = string.Empty;
    public int ItemId { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public DateTime? ManufacturedDate { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Navigation properties
    public Item Item { get; private set; } = null!;

    public void UpdateDates(DateTime? expiryDate = null, DateTime? manufacturedDate = null)
    {
        var normalizedExpiryDate = NormalizeDateOnly(expiryDate);
        var normalizedManufacturedDate = NormalizeDateOnly(manufacturedDate);
        if (normalizedExpiryDate.HasValue && normalizedManufacturedDate.HasValue &&
            normalizedExpiryDate < normalizedManufacturedDate)
            throw new ArgumentException("Expiry date cannot be before manufactured date");

        ExpiryDate = normalizedExpiryDate;
        ManufacturedDate = normalizedManufacturedDate;
        SetUpdatedAt();
    }

    public bool IsExpired(DateOnly businessDate)
    {
        return ExpiryDate.HasValue &&
               DateOnly.FromDateTime(ExpiryDate.Value) < businessDate;
    }

    public bool IsExpiringSoon(DateOnly businessDate, int warningDays = 30)
    {
        return ExpiryDate.HasValue &&
               DateOnly.FromDateTime(ExpiryDate.Value) <= businessDate.AddDays(warningDays);
    }

    private static DateTime? NormalizeDateOnly(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified)
            : null;
}
