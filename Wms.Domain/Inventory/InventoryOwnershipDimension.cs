using Wms.Domain.Enums;

namespace Wms.Domain.Inventory;

public static class InventoryOwnershipDimension
{
    public const string CompanyOwnerCode = "COMPANY";

    public static string NormalizeOwnerCode(
        InventoryOwnerKind ownerKind,
        int? inventoryOwnerId,
        string? ownerCode)
    {
        if (!Enum.IsDefined(ownerKind))
        {
            throw new ArgumentOutOfRangeException(nameof(ownerKind));
        }

        if (ownerKind == InventoryOwnerKind.CompanyOwned)
        {
            if (inventoryOwnerId.HasValue)
            {
                throw new ArgumentException(
                    "Company-owned inventory cannot reference an external owner record.",
                    nameof(inventoryOwnerId));
            }

            return CompanyOwnerCode;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            inventoryOwnerId ?? 0,
            nameof(inventoryOwnerId));
        if (string.IsNullOrWhiteSpace(ownerCode))
        {
            throw new ArgumentException(
                "A non-company inventory owner requires an immutable owner code snapshot.",
                nameof(ownerCode));
        }

        var normalized = ownerCode.Trim().ToUpperInvariant();
        return normalized.Length <= 80
            ? normalized
            : throw new ArgumentException(
                "The inventory owner code snapshot cannot exceed 80 characters.",
                nameof(ownerCode));
    }
}
