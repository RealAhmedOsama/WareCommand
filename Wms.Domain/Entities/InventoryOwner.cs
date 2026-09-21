using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class InventoryOwner : Entity
{
    private InventoryOwner()
    {
    }

    public InventoryOwner(
        string ownerCode,
        InventoryOwnerKind kind,
        string displayName,
        string? localizedName = null,
        int? supplierId = null,
        int? customerId = null,
        string? externalOwnerReference = null)
    {
        if (!Enum.IsDefined(kind) || kind == InventoryOwnerKind.CompanyOwned)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        OwnerCode = Required(ownerCode, 80, nameof(ownerCode)).ToUpperInvariant();
        Kind = kind;
        DisplayName = Required(displayName, 200, nameof(displayName));
        LocalizedName = Optional(localizedName, 200);
        SupplierId = PositiveOptional(supplierId, nameof(supplierId));
        CustomerId = PositiveOptional(customerId, nameof(customerId));
        ExternalOwnerReference = OptionalUpper(externalOwnerReference, 120);
        ValidateOwnerLink(kind, SupplierId, CustomerId, ExternalOwnerReference);
        IsActive = true;
        Revision = 1;
    }

    public string OwnerCode { get; private set; } = string.Empty;
    public InventoryOwnerKind Kind { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? LocalizedName { get; private set; }
    public int? SupplierId { get; private set; }
    public int? CustomerId { get; private set; }
    public string? ExternalOwnerReference { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Supplier? Supplier { get; private set; }
    public Customer? Customer { get; private set; }

    public void UpdateProfile(
        string displayName,
        string? localizedName,
        string? externalOwnerReference)
    {
        DisplayName = Required(displayName, 200, nameof(displayName));
        LocalizedName = Optional(localizedName, 200);
        var normalizedExternalReference = OptionalUpper(externalOwnerReference, 120);
        if (Kind == InventoryOwnerKind.ExternalOwner &&
            !string.Equals(
                ExternalOwnerReference,
                normalizedExternalReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "An external owner reference is immutable after owner creation.");
        }

        ExternalOwnerReference = normalizedExternalReference;
        ValidateOwnerLink(Kind, SupplierId, CustomerId, ExternalOwnerReference);
        Revision++;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        Revision++;
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidateOwnerLink(
        InventoryOwnerKind kind,
        int? supplierId,
        int? customerId,
        string? externalOwnerReference)
    {
        var linkCount = (supplierId.HasValue ? 1 : 0) +
                        (customerId.HasValue ? 1 : 0) +
                        (!string.IsNullOrWhiteSpace(externalOwnerReference) ? 1 : 0);
        if (kind == InventoryOwnerKind.SupplierConsignment &&
            (supplierId is null || customerId.HasValue || !string.IsNullOrWhiteSpace(externalOwnerReference)))
        {
            throw new ArgumentException(
                "Supplier-consigned inventory must link to exactly one supplier.",
                nameof(supplierId));
        }

        if (kind == InventoryOwnerKind.CustomerOwned &&
            (customerId is null || supplierId.HasValue || !string.IsNullOrWhiteSpace(externalOwnerReference)))
        {
            throw new ArgumentException(
                "Customer-owned inventory must link to exactly one customer.",
                nameof(customerId));
        }

        if (kind == InventoryOwnerKind.ExternalOwner &&
            (string.IsNullOrWhiteSpace(externalOwnerReference) || supplierId.HasValue || customerId.HasValue))
        {
            throw new ArgumentException(
                "External-owned inventory must use one external owner reference.",
                nameof(externalOwnerReference));
        }

        if (kind != InventoryOwnerKind.SupplierConsignment &&
            kind != InventoryOwnerKind.CustomerOwned &&
            kind != InventoryOwnerKind.ExternalOwner || linkCount != 1)
        {
            throw new ArgumentException("The inventory owner link is invalid.", nameof(kind));
        }
    }

    private static int? PositiveOptional(int? value, string parameterName) =>
        value is null ? null : value > 0 ? value : throw new ArgumentOutOfRangeException(parameterName);

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
