using System.Globalization;
using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class LicensePlateNumberSequence : Entity
{
    private LicensePlateNumberSequence()
    {
    }

    public LicensePlateNumberSequence(
        int warehouseId,
        string prefix = "LPN-",
        int padding = 8,
        long nextNumber = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        WarehouseId = warehouseId;
        Configure(prefix, padding, nextNumber, stampUpdatedAt: false);
    }

    public int WarehouseId { get; private set; }
    public string Prefix { get; private set; } = "LPN-";
    public int Padding { get; private set; } = 8;
    public long NextNumber { get; private set; } = 1;
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;

    public void Configure(string prefix, int padding, long nextNumber)
    {
        Configure(prefix, padding, nextNumber, stampUpdatedAt: true);
    }

    public string TakeNextNumber()
    {
        var number = Prefix + NextNumber.ToString($"D{Padding}", CultureInfo.InvariantCulture);
        NextNumber = checked(NextNumber + 1);
        Revision = checked(Revision + 1);
        SetUpdatedAt();
        return number;
    }

    private void Configure(string prefix, int padding, long nextNumber, bool stampUpdatedAt)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("A license plate prefix is required.", nameof(prefix));
        }

        var normalizedPrefix = prefix.Trim().ToUpperInvariant();
        if (normalizedPrefix.Length > 20 || normalizedPrefix.Any(char.IsControl))
        {
            throw new ArgumentException("License plate prefix cannot exceed 20 printable characters.", nameof(prefix));
        }

        if (padding is < 1 or > 18)
        {
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding must be between 1 and 18.");
        }

        if (nextNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextNumber), "The next number must be positive.");
        }

        Prefix = normalizedPrefix;
        Padding = padding;
        NextNumber = nextNumber;
        if (stampUpdatedAt)
        {
            Revision = checked(Revision + 1);
            SetUpdatedAt();
        }
    }
}
