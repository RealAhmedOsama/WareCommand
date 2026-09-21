using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// A customer's SKU/barcode for an internal item, used by channel imports and
/// order entry without changing the internal item identity.
/// </summary>
public sealed class CustomerItemReference : Entity
{
    private CustomerItemReference()
    {
    }

    public CustomerItemReference(
        int itemId,
        string customerSku,
        string? customerBarcode = null,
        string? customerDescription = null)
    {
        ItemId = ValidatePositive(itemId, nameof(itemId));
        Apply(customerSku, customerBarcode, customerDescription, stampUpdatedAt: false);
    }

    public int CustomerId { get; private set; }
    public int ItemId { get; private set; }
    public string CustomerSku { get; private set; } = string.Empty;
    public string? CustomerBarcode { get; private set; }
    public string? CustomerDescription { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Customer Customer { get; private set; } = null!;
    public Item Item { get; private set; } = null!;

    public void Update(
        int itemId,
        string customerSku,
        string? customerBarcode,
        string? customerDescription,
        bool isActive)
    {
        ItemId = ValidatePositive(itemId, nameof(itemId));
        Apply(customerSku, customerBarcode, customerDescription, stampUpdatedAt: true);
        IsActive = isActive;
        SetUpdatedAt();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        SetUpdatedAt();
    }

    private void Apply(
        string customerSku,
        string? customerBarcode,
        string? customerDescription,
        bool stampUpdatedAt)
    {
        CustomerSku = NormalizeRequired(customerSku, 100, nameof(customerSku), uppercase: true);
        CustomerBarcode = NormalizeOptionalUpper(customerBarcode, 100);
        CustomerDescription = NormalizeOptional(customerDescription, 200);
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static int ValidatePositive(int value, string parameterName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static string NormalizeRequired(string value, int maximumLength, string parameterName, bool uppercase = false)
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
