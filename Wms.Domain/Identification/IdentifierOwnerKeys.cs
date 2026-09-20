using System.Globalization;

namespace Wms.Domain.Identification;

public static class IdentifierOwnerKeys
{
    public static string Item(string sku) => $"ITEM:{Normalize(sku)}";

    public static string Packaging(string sku, string code) =>
        $"PACKAGING:{Normalize(sku)}|{Normalize(code)}";

    public static string Location(int warehouseId, string code) =>
        $"LOCATION:{warehouseId.ToString(CultureInfo.InvariantCulture)}|{Normalize(code)}";

    public static string Lot(int itemId, string number) =>
        $"LOT:{itemId.ToString(CultureInfo.InvariantCulture)}|{Normalize(number)}";

    public static string Serial(int itemId, string serialNumber) =>
        $"SERIAL:{itemId.ToString(CultureInfo.InvariantCulture)}|{Normalize(serialNumber)}";

    public static string LicensePlate(string value) => $"LPN:{Normalize(value)}";

    public static string Document(string value) => $"DOCUMENT:{Normalize(value)}";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("An identifier owner value is required.", nameof(value))
            : value.Trim().ToUpperInvariant();
}
