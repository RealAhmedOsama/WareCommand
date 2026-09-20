using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class InventoryStatus : Entity
{
    private InventoryStatus()
    {
    }

    public InventoryStatus(
        string code,
        string name,
        string localizedName,
        bool isAvailable,
        bool isAllocatable,
        bool isPickable,
        bool isShippable,
        bool isCountable,
        int? warehouseId = null,
        bool isSystem = false,
        LocationType? defaultLocationType = null,
        bool forceForLocationType = false)
    {
        Code = Normalize(code, 50, nameof(code), uppercase: true);
        Name = Normalize(name, 200, nameof(name));
        LocalizedName = Normalize(localizedName, 200, nameof(localizedName));
        if (warehouseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId));
        }

        WarehouseId = warehouseId;
        IsSystem = isSystem;
        ApplyRules(
            isAvailable,
            isAllocatable,
            isPickable,
            isShippable,
            isCountable,
            defaultLocationType,
            forceForLocationType,
            stampUpdatedAt: false);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocalizedName { get; private set; } = string.Empty;
    public int? WarehouseId { get; private set; }
    public bool IsAvailable { get; private set; }
    public bool IsAllocatable { get; private set; }
    public bool IsPickable { get; private set; }
    public bool IsShippable { get; private set; }
    public bool IsCountable { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsSystem { get; private set; }
    public LocationType? DefaultLocationType { get; private set; }
    public bool ForceForLocationType { get; private set; }

    public Warehouse? Warehouse { get; private set; }

    public void Update(
        string name,
        string localizedName,
        bool isAvailable,
        bool isAllocatable,
        bool isPickable,
        bool isShippable,
        bool isCountable,
        LocationType? defaultLocationType,
        bool forceForLocationType)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System inventory statuses cannot be redefined.");
        }

        Name = Normalize(name, 200, nameof(name));
        LocalizedName = Normalize(localizedName, 200, nameof(localizedName));
        ApplyRules(
            isAvailable,
            isAllocatable,
            isPickable,
            isShippable,
            isCountable,
            defaultLocationType,
            forceForLocationType,
            stampUpdatedAt: true);
    }

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System inventory statuses cannot be disabled.");
        }

        IsActive = false;
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdatedAt();
    }

    public bool AppliesTo(LocationType locationType) =>
        DefaultLocationType == locationType;

    private void ApplyRules(
        bool isAvailable,
        bool isAllocatable,
        bool isPickable,
        bool isShippable,
        bool isCountable,
        LocationType? defaultLocationType,
        bool forceForLocationType,
        bool stampUpdatedAt)
    {
        if (isAllocatable && !isAvailable)
        {
            throw new ArgumentException(
                "An allocatable status must also be available.",
                nameof(isAllocatable));
        }

        IsAvailable = isAvailable;
        IsAllocatable = isAllocatable;
        IsPickable = isPickable;
        IsShippable = isShippable;
        IsCountable = isCountable;
        DefaultLocationType = defaultLocationType;
        ForceForLocationType = forceForLocationType;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static string Normalize(string value, int maximumLength, string parameterName, bool uppercase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (uppercase)
        {
            normalized = normalized.ToUpperInvariant();
        }

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }
}
