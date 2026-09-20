using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Supplier master data and the defaults used when a receiving document does
/// not provide an explicit override. Supplier changes are intentionally kept
/// separate from receiving documents so future documents can snapshot the
/// values they used.
/// </summary>
public sealed class Supplier : Entity
{
    private readonly List<SupplierItemReference> _itemReferences = new();

    private Supplier()
    {
    }

    public Supplier(
        string code,
        string legalName,
        string? localizedName = null,
        string? taxRegistrationNumber = null,
        string? externalErpIdentifier = null,
        string? addressLine1 = null,
        string? addressLine2 = null,
        string? city = null,
        string? stateOrProvince = null,
        string? postalCode = null,
        string? countryCode = null,
        string? contactName = null,
        string? contactEmail = null,
        string? contactPhone = null,
        int? preferredWarehouseId = null,
        int? preferredDockLocationId = null,
        int? defaultLeadTimeDays = null,
        decimal? overDeliveryTolerancePercent = null,
        decimal? underDeliveryTolerancePercent = null,
        bool requiresLot = false,
        bool requiresExpiry = false,
        string? qualityProfile = null,
        string? labelRule = null,
        string? defaultCurrencyCode = null,
        string? notes = null)
    {
        Code = NormalizeRequired(code, 50, nameof(code), uppercase: true);
        ApplyProfile(
            legalName,
            localizedName,
            taxRegistrationNumber,
            externalErpIdentifier,
            addressLine1,
            addressLine2,
            city,
            stateOrProvince,
            postalCode,
            countryCode,
            contactName,
            contactEmail,
            contactPhone,
            preferredWarehouseId,
            preferredDockLocationId,
            defaultLeadTimeDays,
            overDeliveryTolerancePercent,
            underDeliveryTolerancePercent,
            requiresLot,
            requiresExpiry,
            qualityProfile,
            labelRule,
            defaultCurrencyCode,
            notes,
            stampUpdatedAt: false);
    }

    public string Code { get; private set; } = string.Empty;
    public string LegalName { get; private set; } = string.Empty;
    public string? LocalizedName { get; private set; }
    public string? TaxRegistrationNumber { get; private set; }
    public string? ExternalErpIdentifier { get; private set; }
    public string? AddressLine1 { get; private set; }
    public string? AddressLine2 { get; private set; }
    public string? City { get; private set; }
    public string? StateOrProvince { get; private set; }
    public string? PostalCode { get; private set; }
    public string? CountryCode { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public int? PreferredWarehouseId { get; private set; }
    public int? PreferredDockLocationId { get; private set; }
    public int? DefaultLeadTimeDays { get; private set; }
    public decimal? OverDeliveryTolerancePercent { get; private set; }
    public decimal? UnderDeliveryTolerancePercent { get; private set; }
    public bool RequiresLot { get; private set; }
    public bool RequiresExpiry { get; private set; }
    public string? QualityProfile { get; private set; }
    public string? LabelRule { get; private set; }
    public string? DefaultCurrencyCode { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyList<SupplierItemReference> ItemReferences => _itemReferences.AsReadOnly();

    public void UpdateProfile(
        string legalName,
        string? localizedName,
        string? taxRegistrationNumber,
        string? externalErpIdentifier,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? stateOrProvince,
        string? postalCode,
        string? countryCode,
        string? contactName,
        string? contactEmail,
        string? contactPhone,
        int? preferredWarehouseId,
        int? preferredDockLocationId,
        int? defaultLeadTimeDays,
        decimal? overDeliveryTolerancePercent,
        decimal? underDeliveryTolerancePercent,
        bool requiresLot,
        bool requiresExpiry,
        string? qualityProfile,
        string? labelRule,
        string? defaultCurrencyCode,
        string? notes)
    {
        ApplyProfile(
            legalName,
            localizedName,
            taxRegistrationNumber,
            externalErpIdentifier,
            addressLine1,
            addressLine2,
            city,
            stateOrProvince,
            postalCode,
            countryCode,
            contactName,
            contactEmail,
            contactPhone,
            preferredWarehouseId,
            preferredDockLocationId,
            defaultLeadTimeDays,
            overDeliveryTolerancePercent,
            underDeliveryTolerancePercent,
            requiresLot,
            requiresExpiry,
            qualityProfile,
            labelRule,
            defaultCurrencyCode,
            notes,
            stampUpdatedAt: true);
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    public void AddItemReference(SupplierItemReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_itemReferences.Contains(reference))
        {
            throw new InvalidOperationException("The supplier item reference is already attached.");
        }

        _itemReferences.Add(reference);
        SetUpdatedAt();
    }

    public void RemoveItemReference(SupplierItemReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_itemReferences.Remove(reference))
        {
            throw new InvalidOperationException("The supplier item reference is not attached.");
        }

        SetUpdatedAt();
    }

    private void ApplyProfile(
        string legalName,
        string? localizedName,
        string? taxRegistrationNumber,
        string? externalErpIdentifier,
        string? addressLine1,
        string? addressLine2,
        string? city,
        string? stateOrProvince,
        string? postalCode,
        string? countryCode,
        string? contactName,
        string? contactEmail,
        string? contactPhone,
        int? preferredWarehouseId,
        int? preferredDockLocationId,
        int? defaultLeadTimeDays,
        decimal? overDeliveryTolerancePercent,
        decimal? underDeliveryTolerancePercent,
        bool requiresLot,
        bool requiresExpiry,
        string? qualityProfile,
        string? labelRule,
        string? defaultCurrencyCode,
        string? notes,
        bool stampUpdatedAt)
    {
        LegalName = NormalizeRequired(legalName, 200, nameof(legalName));
        LocalizedName = NormalizeOptional(localizedName, 200);
        TaxRegistrationNumber = NormalizeOptionalUpper(taxRegistrationNumber, 100);
        ExternalErpIdentifier = NormalizeOptionalUpper(externalErpIdentifier, 100);
        AddressLine1 = NormalizeOptional(addressLine1, 200);
        AddressLine2 = NormalizeOptional(addressLine2, 200);
        City = NormalizeOptional(city, 100);
        StateOrProvince = NormalizeOptional(stateOrProvince, 100);
        PostalCode = NormalizeOptional(postalCode, 30);
        CountryCode = NormalizeCode(countryCode, 2, nameof(countryCode));
        ContactName = NormalizeOptional(contactName, 200);
        ContactEmail = NormalizeEmail(contactEmail);
        ContactPhone = NormalizeOptional(contactPhone, 50);

        if (preferredWarehouseId is <= 0 || preferredDockLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredWarehouseId),
                "Preferred warehouse and dock identifiers must be positive when provided.");
        }

        if (preferredDockLocationId.HasValue && !preferredWarehouseId.HasValue)
        {
            throw new ArgumentException(
                "A preferred dock requires a preferred warehouse.",
                nameof(preferredDockLocationId));
        }

        PreferredWarehouseId = preferredWarehouseId;
        PreferredDockLocationId = preferredDockLocationId;
        DefaultLeadTimeDays = ValidateNonNegative(defaultLeadTimeDays, 3_650, nameof(defaultLeadTimeDays));
        OverDeliveryTolerancePercent = ValidatePercentage(
            overDeliveryTolerancePercent,
            nameof(overDeliveryTolerancePercent));
        UnderDeliveryTolerancePercent = ValidatePercentage(
            underDeliveryTolerancePercent,
            nameof(underDeliveryTolerancePercent));
        RequiresLot = requiresLot;
        RequiresExpiry = requiresExpiry;
        QualityProfile = NormalizeOptional(qualityProfile, 100);
        LabelRule = NormalizeOptional(labelRule, 100);
        DefaultCurrencyCode = NormalizeCurrency(defaultCurrencyCode);
        Notes = NormalizeOptional(notes, 2_000);

        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static int? ValidateNonNegative(int? value, int maximum, string parameterName)
    {
        if (value.HasValue && (value.Value < 0 || value.Value > maximum))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value must be between 0 and {maximum}.");
        }

        return value;
    }

    private static decimal? ValidatePercentage(decimal? value, string parameterName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The tolerance must be between 0 and 100 percent.");
        }

        return value;
    }

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

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
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

    private static string? NormalizeOptionalUpper(string? value, int maximumLength)
    {
        var normalized = NormalizeOptional(value, maximumLength);
        return normalized?.ToUpperInvariant();
    }

    private static string? NormalizeCode(string? value, int maximumLength, string parameterName)
    {
        var normalized = NormalizeOptionalUpper(value, maximumLength);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                $"The {parameterName} must contain only letters.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeCurrency(string? value)
    {
        var normalized = NormalizeOptionalUpper(value, 3);
        if (normalized is not null &&
            (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z')))
        {
            throw new ArgumentException(
                "The default currency must be a three-letter ISO-style code.",
                nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeEmail(string? value)
    {
        var normalized = NormalizeOptional(value, 320);
        if (normalized is not null &&
            (!normalized.Contains('@', StringComparison.Ordinal) ||
             normalized.StartsWith('@') ||
             normalized.EndsWith('@')))
        {
            throw new ArgumentException("The contact email is invalid.", nameof(value));
        }

        return normalized;
    }
}
