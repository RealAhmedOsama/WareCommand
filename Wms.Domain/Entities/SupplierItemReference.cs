using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// The supplier-facing identifiers for one internal item. These mappings are
/// scoped to a supplier so an external SKU or barcode can be resolved safely
/// during future ASN/receipt imports.
/// </summary>
public sealed class SupplierItemReference : Entity
{
    private SupplierItemReference()
    {
    }

    public SupplierItemReference(
        int itemId,
        string vendorSku,
        string? vendorBarcode = null,
        int? itemPackagingId = null,
        string? vendorPackaging = null,
        decimal? unitsPerPurchasePackage = null,
        decimal minimumOrderQuantity = 1,
        int? leadTimeDays = null)
    {
        ItemId = ValidatePositive(itemId, nameof(itemId));
        Apply(
            vendorSku,
            vendorBarcode,
            itemPackagingId,
            vendorPackaging,
            unitsPerPurchasePackage,
            minimumOrderQuantity,
            leadTimeDays,
            stampUpdatedAt: false);
    }

    public int SupplierId { get; private set; }
    public int ItemId { get; private set; }
    public int? ItemPackagingId { get; private set; }
    public string VendorSku { get; private set; } = string.Empty;
    public string? VendorBarcode { get; private set; }
    public string? VendorPackaging { get; private set; }
    public decimal? UnitsPerPurchasePackage { get; private set; }
    public decimal MinimumOrderQuantity { get; private set; } = 1;
    public int? LeadTimeDays { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Supplier Supplier { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public ItemPackaging? ItemPackaging { get; private set; }

    public void Update(
        int itemId,
        string vendorSku,
        string? vendorBarcode,
        int? itemPackagingId,
        string? vendorPackaging,
        decimal? unitsPerPurchasePackage,
        decimal minimumOrderQuantity,
        int? leadTimeDays)
    {
        ItemId = ValidatePositive(itemId, nameof(itemId));
        Apply(
            vendorSku,
            vendorBarcode,
            itemPackagingId,
            vendorPackaging,
            unitsPerPurchasePackage,
            minimumOrderQuantity,
            leadTimeDays,
            stampUpdatedAt: true);
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        SetUpdatedAt();
    }

    private void Apply(
        string vendorSku,
        string? vendorBarcode,
        int? itemPackagingId,
        string? vendorPackaging,
        decimal? unitsPerPurchasePackage,
        decimal minimumOrderQuantity,
        int? leadTimeDays,
        bool stampUpdatedAt)
    {
        VendorSku = NormalizeRequired(vendorSku, 100, nameof(vendorSku), uppercase: true);
        VendorBarcode = NormalizeOptionalUpper(vendorBarcode, 100);
        VendorPackaging = NormalizeOptional(vendorPackaging, 100);
        ItemPackagingId = itemPackagingId is <= 0 ? throw new ArgumentOutOfRangeException(nameof(itemPackagingId)) : itemPackagingId;
        if (unitsPerPurchasePackage is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitsPerPurchasePackage),
                "Units per purchase package must be positive when supplied.");
        }

        if (minimumOrderQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumOrderQuantity),
                "Minimum order quantity must be positive.");
        }

        if (leadTimeDays is < 0 or > 3_650)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadTimeDays),
                "Lead time must be between 0 and 3650 days.");
        }

        UnitsPerPurchasePackage = unitsPerPurchasePackage;
        MinimumOrderQuantity = minimumOrderQuantity;
        LeadTimeDays = leadTimeDays;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static int ValidatePositive(int value, string parameterName) =>
        value > 0
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName,
        bool uppercase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (uppercase)
        {
            normalized = normalized.ToUpperInvariant();
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();
}
