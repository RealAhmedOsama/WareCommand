using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Customer master data used by outbound demand. Confirmed documents must copy
/// the values they use because this record remains editable for future orders.
/// </summary>
public sealed class Customer : Entity
{
    private readonly List<CustomerShipToAddress> _shipToAddresses = new();
    private readonly List<CustomerItemReference> _itemReferences = new();

    private Customer()
    {
    }

    public Customer(
        string code,
        string legalName,
        string? localizedName = null,
        string? taxRegistrationNumber = null,
        string? externalErpIdentifier = null,
        string? externalChannelIdentifier = null,
        string? contactName = null,
        string? contactEmail = null,
        string? contactPhone = null,
        string? billingAddressLine1 = null,
        string? billingAddressLine2 = null,
        string? billingCity = null,
        string? billingRegion = null,
        string? billingPostalCode = null,
        string? billingCountryCode = null,
        string? defaultCarrierCode = null,
        string? defaultCarrierServiceCode = null,
        int priority = 100,
        string? packagingProfile = null,
        string? labelProfile = null,
        bool allowPartialShipment = false,
        string? notes = null)
    {
        Code = NormalizeRequired(code, 50, nameof(code), uppercase: true);
        ApplyProfile(
            legalName,
            localizedName,
            taxRegistrationNumber,
            externalErpIdentifier,
            externalChannelIdentifier,
            contactName,
            contactEmail,
            contactPhone,
            billingAddressLine1,
            billingAddressLine2,
            billingCity,
            billingRegion,
            billingPostalCode,
            billingCountryCode,
            defaultCarrierCode,
            defaultCarrierServiceCode,
            priority,
            packagingProfile,
            labelProfile,
            allowPartialShipment,
            notes,
            stampUpdatedAt: false);
    }

    public string Code { get; private set; } = string.Empty;
    public string LegalName { get; private set; } = string.Empty;
    public string? LocalizedName { get; private set; }
    public string? TaxRegistrationNumber { get; private set; }
    public string? ExternalErpIdentifier { get; private set; }
    public string? ExternalChannelIdentifier { get; private set; }
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? BillingAddressLine1 { get; private set; }
    public string? BillingAddressLine2 { get; private set; }
    public string? BillingCity { get; private set; }
    public string? BillingRegion { get; private set; }
    public string? BillingPostalCode { get; private set; }
    public string? BillingCountryCode { get; private set; }
    public string? DefaultCarrierCode { get; private set; }
    public string? DefaultCarrierServiceCode { get; private set; }
    public int Priority { get; private set; } = 100;
    public string? PackagingProfile { get; private set; }
    public string? LabelProfile { get; private set; }
    public bool AllowPartialShipment { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public IReadOnlyList<CustomerShipToAddress> ShipToAddresses => _shipToAddresses.AsReadOnly();
    public IReadOnlyList<CustomerItemReference> ItemReferences => _itemReferences.AsReadOnly();

    public void UpdateProfile(
        string legalName,
        string? localizedName,
        string? taxRegistrationNumber,
        string? externalErpIdentifier,
        string? externalChannelIdentifier,
        string? contactName,
        string? contactEmail,
        string? contactPhone,
        string? billingAddressLine1,
        string? billingAddressLine2,
        string? billingCity,
        string? billingRegion,
        string? billingPostalCode,
        string? billingCountryCode,
        string? defaultCarrierCode,
        string? defaultCarrierServiceCode,
        int priority,
        string? packagingProfile,
        string? labelProfile,
        bool allowPartialShipment,
        string? notes)
    {
        ApplyProfile(
            legalName,
            localizedName,
            taxRegistrationNumber,
            externalErpIdentifier,
            externalChannelIdentifier,
            contactName,
            contactEmail,
            contactPhone,
            billingAddressLine1,
            billingAddressLine2,
            billingCity,
            billingRegion,
            billingPostalCode,
            billingCountryCode,
            defaultCarrierCode,
            defaultCarrierServiceCode,
            priority,
            packagingProfile,
            labelProfile,
            allowPartialShipment,
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

    public void AddShipToAddress(CustomerShipToAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (_shipToAddresses.Contains(address))
        {
            throw new InvalidOperationException("The ship-to address is already attached.");
        }

        if (_shipToAddresses.Count == 0)
        {
            address.SetDefault(true);
        }

        if (address.IsDefault)
        {
            foreach (var existing in _shipToAddresses)
            {
                existing.SetDefault(false);
            }
        }

        _shipToAddresses.Add(address);
        SetUpdatedAt();
    }

    public void RemoveShipToAddress(CustomerShipToAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!_shipToAddresses.Remove(address))
        {
            throw new InvalidOperationException("The ship-to address is not attached.");
        }

        if (address.IsDefault && _shipToAddresses.Count > 0)
        {
            _shipToAddresses.OrderBy(candidate => candidate.Code, StringComparer.Ordinal)
                .First()
                .SetDefault(true);
        }

        SetUpdatedAt();
    }

    public void SetDefaultShipTo(int addressId)
    {
        var selected = _shipToAddresses.SingleOrDefault(address => address.Id == addressId);
        if (selected is null)
        {
            throw new InvalidOperationException("The selected ship-to address is not attached.");
        }

        foreach (var address in _shipToAddresses)
        {
            address.SetDefault(address == selected);
        }

        SetUpdatedAt();
    }

    public void AddItemReference(CustomerItemReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_itemReferences.Contains(reference))
        {
            throw new InvalidOperationException("The customer item reference is already attached.");
        }

        _itemReferences.Add(reference);
        SetUpdatedAt();
    }

    public void RemoveItemReference(CustomerItemReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!_itemReferences.Remove(reference))
        {
            throw new InvalidOperationException("The customer item reference is not attached.");
        }

        SetUpdatedAt();
    }

    private void ApplyProfile(
        string legalName,
        string? localizedName,
        string? taxRegistrationNumber,
        string? externalErpIdentifier,
        string? externalChannelIdentifier,
        string? contactName,
        string? contactEmail,
        string? contactPhone,
        string? billingAddressLine1,
        string? billingAddressLine2,
        string? billingCity,
        string? billingRegion,
        string? billingPostalCode,
        string? billingCountryCode,
        string? defaultCarrierCode,
        string? defaultCarrierServiceCode,
        int priority,
        string? packagingProfile,
        string? labelProfile,
        bool allowPartialShipment,
        string? notes,
        bool stampUpdatedAt)
    {
        LegalName = NormalizeRequired(legalName, 200, nameof(legalName));
        LocalizedName = NormalizeOptional(localizedName, 200);
        TaxRegistrationNumber = NormalizeOptionalUpper(taxRegistrationNumber, 100);
        ExternalErpIdentifier = NormalizeOptionalUpper(externalErpIdentifier, 100);
        ExternalChannelIdentifier = NormalizeOptionalUpper(externalChannelIdentifier, 100);
        ContactName = NormalizeOptional(contactName, 200);
        ContactEmail = NormalizeEmail(contactEmail);
        ContactPhone = NormalizeOptional(contactPhone, 50);
        BillingAddressLine1 = NormalizeOptional(billingAddressLine1, 200);
        BillingAddressLine2 = NormalizeOptional(billingAddressLine2, 200);
        BillingCity = NormalizeOptional(billingCity, 100);
        BillingRegion = NormalizeOptional(billingRegion, 100);
        BillingPostalCode = NormalizeOptional(billingPostalCode, 30);
        BillingCountryCode = NormalizeCountryCode(billingCountryCode, nameof(billingCountryCode));
        DefaultCarrierCode = NormalizeOptionalUpper(defaultCarrierCode, 50);
        DefaultCarrierServiceCode = NormalizeOptionalUpper(defaultCarrierServiceCode, 80);
        if (priority is < 0 or > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be between 0 and 999.");
        }

        Priority = priority;
        PackagingProfile = NormalizeOptional(packagingProfile, 100);
        LabelProfile = NormalizeOptional(labelProfile, 100);
        AllowPartialShipment = allowPartialShipment;
        Notes = NormalizeOptional(notes, 2_000);
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

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();

    private static string? NormalizeCountryCode(string? value, string parameterName)
    {
        var normalized = NormalizeOptionalUpper(value, 2);
        if (normalized is not null &&
            (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z')))
        {
            throw new ArgumentException("The country code must contain exactly two letters.", parameterName);
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
