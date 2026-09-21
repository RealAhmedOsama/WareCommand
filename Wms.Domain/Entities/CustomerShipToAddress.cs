using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// A customer delivery destination. It is never deleted by master-data edits
/// when a future outbound document references it; those document foreign keys
/// use restrict semantics and retain the historical snapshot separately.
/// </summary>
public sealed class CustomerShipToAddress : Entity
{
    private CustomerShipToAddress()
    {
    }

    public CustomerShipToAddress(
        string code,
        string recipientName,
        string? phone,
        string? countryCode,
        string? region,
        string? city,
        string? postalCode,
        string addressLine1,
        string? addressLine2 = null,
        string? deliveryInstructions = null,
        TimeSpan? deliveryWindowStart = null,
        TimeSpan? deliveryWindowEnd = null,
        bool isDefault = false)
    {
        Code = NormalizeRequired(code, 50, nameof(code), uppercase: true);
        Apply(
            recipientName,
            phone,
            countryCode,
            region,
            city,
            postalCode,
            addressLine1,
            addressLine2,
            deliveryInstructions,
            deliveryWindowStart,
            deliveryWindowEnd,
            isDefault,
            stampUpdatedAt: false);
    }

    public int CustomerId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string RecipientName { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? CountryCode { get; private set; }
    public string? Region { get; private set; }
    public string? City { get; private set; }
    public string? PostalCode { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string? DeliveryInstructions { get; private set; }
    public TimeSpan? DeliveryWindowStart { get; private set; }
    public TimeSpan? DeliveryWindowEnd { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Customer Customer { get; private set; } = null!;

    public void Update(
        string recipientName,
        string? phone,
        string? countryCode,
        string? region,
        string? city,
        string? postalCode,
        string addressLine1,
        string? addressLine2,
        string? deliveryInstructions,
        TimeSpan? deliveryWindowStart,
        TimeSpan? deliveryWindowEnd,
        bool isDefault,
        bool isActive)
    {
        Apply(
            recipientName,
            phone,
            countryCode,
            region,
            city,
            postalCode,
            addressLine1,
            addressLine2,
            deliveryInstructions,
            deliveryWindowStart,
            deliveryWindowEnd,
            isDefault,
            stampUpdatedAt: true);
        IsActive = isActive;
        SetUpdatedAt();
    }

    public void SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        SetUpdatedAt();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        SetUpdatedAt();
    }

    private void Apply(
        string recipientName,
        string? phone,
        string? countryCode,
        string? region,
        string? city,
        string? postalCode,
        string addressLine1,
        string? addressLine2,
        string? deliveryInstructions,
        TimeSpan? deliveryWindowStart,
        TimeSpan? deliveryWindowEnd,
        bool isDefault,
        bool stampUpdatedAt)
    {
        RecipientName = NormalizeRequired(recipientName, 200, nameof(recipientName));
        Phone = NormalizeOptional(phone, 50);
        CountryCode = NormalizeCountryCode(countryCode);
        Region = NormalizeOptional(region, 100);
        City = NormalizeOptional(city, 100);
        PostalCode = NormalizeOptional(postalCode, 30);
        AddressLine1 = NormalizeRequired(addressLine1, 200, nameof(addressLine1));
        AddressLine2 = NormalizeOptional(addressLine2, 200);
        DeliveryInstructions = NormalizeOptional(deliveryInstructions, 1_000);
        if (deliveryWindowStart.HasValue != deliveryWindowEnd.HasValue)
        {
            throw new ArgumentException("Both delivery-window times are required when one is supplied.");
        }

        if (deliveryWindowStart.HasValue && deliveryWindowStart > deliveryWindowEnd)
        {
            throw new ArgumentException("The delivery window start must not be after its end.");
        }

        DeliveryWindowStart = deliveryWindowStart;
        DeliveryWindowEnd = deliveryWindowEnd;
        IsDefault = isDefault;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

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

    private static string? NormalizeCountryCode(string? value)
    {
        var normalized = NormalizeOptional(value, 2)?.ToUpperInvariant();
        if (normalized is not null &&
            (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z')))
        {
            throw new ArgumentException("The country code must contain exactly two letters.", nameof(value));
        }

        return normalized;
    }
}
