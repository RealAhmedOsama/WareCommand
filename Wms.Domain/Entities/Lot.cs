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

        Number = number.Trim().ToUpperInvariant();
        ItemId = itemId;
        ExpiryDate = expiryDate;
        ManufacturedDate = manufacturedDate;

        if (expiryDate.HasValue && manufacturedDate.HasValue && expiryDate < manufacturedDate)
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
        if (expiryDate.HasValue && manufacturedDate.HasValue && expiryDate < manufacturedDate)
            throw new ArgumentException("Expiry date cannot be before manufactured date");

        ExpiryDate = expiryDate;
        ManufacturedDate = manufacturedDate;
        SetUpdatedAt();
    }

    public bool IsExpired()
    {
        return ExpiryDate.HasValue && ExpiryDate.Value.Date < DateTime.UtcNow.Date;
    }

    public bool IsExpiringSoon(int warningDays = 30)
    {
        return ExpiryDate.HasValue &&
               ExpiryDate.Value.Date <= DateTime.UtcNow.Date.AddDays(warningDays);
    }
}
