using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Effective-dated replenishment policy for an item in a warehouse, optionally
/// narrowed to one location. The policy produces signals only; it never mutates
/// inventory or creates a purchase order.
/// </summary>
public sealed class InventoryReplenishmentPolicy : Entity
{
    private InventoryReplenishmentPolicy()
    {
    }

    public InventoryReplenishmentPolicy(
        int itemId,
        int warehouseId,
        int? locationId,
        decimal minimumQuantity,
        decimal maximumQuantity,
        decimal safetyStockQuantity,
        decimal reorderPointQuantity,
        decimal targetQuantity,
        InventoryPolicyQuantityBasis quantityBasis,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        string? preferredSource = null,
        int? leadTimeDays = null)
    {
        ValidateIdentity(itemId, warehouseId, locationId);
        Apply(
            minimumQuantity,
            maximumQuantity,
            safetyStockQuantity,
            reorderPointQuantity,
            targetQuantity,
            quantityBasis,
            effectiveFromUtc,
            effectiveToUtc,
            preferredSource,
            leadTimeDays,
            stampUpdatedAt: false);
        ItemId = itemId;
        WarehouseId = warehouseId;
        LocationId = locationId;
        IsActive = true;
    }

    public int ItemId { get; private set; }
    public int WarehouseId { get; private set; }
    public int? LocationId { get; private set; }
    public decimal MinimumQuantity { get; private set; }
    public decimal MaximumQuantity { get; private set; }
    public decimal SafetyStockQuantity { get; private set; }
    public decimal ReorderPointQuantity { get; private set; }
    public decimal TargetQuantity { get; private set; }
    public InventoryPolicyQuantityBasis QuantityBasis { get; private set; }
    public string? PreferredSource { get; private set; }
    public int? LeadTimeDays { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Item Item { get; private set; } = null!;
    public Warehouse Warehouse { get; private set; } = null!;
    public Location? Location { get; private set; }

    public bool IsEffectiveAt(DateTime utcNow) =>
        IsActive &&
        EffectiveFromUtc <= utcNow &&
        (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > utcNow);

    public void Update(
        decimal minimumQuantity,
        decimal maximumQuantity,
        decimal safetyStockQuantity,
        decimal reorderPointQuantity,
        decimal targetQuantity,
        InventoryPolicyQuantityBasis quantityBasis,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        string? preferredSource = null,
        int? leadTimeDays = null)
    {
        Apply(
            minimumQuantity,
            maximumQuantity,
            safetyStockQuantity,
            reorderPointQuantity,
            targetQuantity,
            quantityBasis,
            effectiveFromUtc,
            effectiveToUtc,
            preferredSource,
            leadTimeDays,
            stampUpdatedAt: true);
        Revision++;
    }

    public void Activate()
    {
        IsActive = true;
        Revision++;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        Revision++;
        SetUpdatedAt();
    }

    private void Apply(
        decimal minimumQuantity,
        decimal maximumQuantity,
        decimal safetyStockQuantity,
        decimal reorderPointQuantity,
        decimal targetQuantity,
        InventoryPolicyQuantityBasis quantityBasis,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        string? preferredSource,
        int? leadTimeDays,
        bool stampUpdatedAt)
    {
        ValidateQuantities(
            minimumQuantity,
            maximumQuantity,
            safetyStockQuantity,
            reorderPointQuantity,
            targetQuantity);
        if (!Enum.IsDefined(quantityBasis))
        {
            throw new ArgumentOutOfRangeException(nameof(quantityBasis));
        }

        var from = Entity.NormalizeUtc(effectiveFromUtc);
        var to = effectiveToUtc.HasValue ? Entity.NormalizeUtc(effectiveToUtc.Value) : (DateTime?)null;
        if (to.HasValue && to.Value <= from)
        {
            throw new ArgumentException(
                "The policy effective end must be after its effective start.",
                nameof(effectiveToUtc));
        }

        if (leadTimeDays is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leadTimeDays));
        }

        MinimumQuantity = minimumQuantity;
        MaximumQuantity = maximumQuantity;
        SafetyStockQuantity = safetyStockQuantity;
        ReorderPointQuantity = reorderPointQuantity;
        TargetQuantity = targetQuantity;
        QuantityBasis = quantityBasis;
        EffectiveFromUtc = from;
        EffectiveToUtc = to;
        PreferredSource = NormalizeOptional(preferredSource, 200);
        LeadTimeDays = leadTimeDays;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static void ValidateIdentity(int itemId, int warehouseId, int? locationId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (locationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(locationId));
        }
    }

    private static void ValidateQuantities(
        decimal minimumQuantity,
        decimal maximumQuantity,
        decimal safetyStockQuantity,
        decimal reorderPointQuantity,
        decimal targetQuantity)
    {
        if (minimumQuantity < 0m || maximumQuantity < 0m || safetyStockQuantity < 0m ||
            reorderPointQuantity < 0m || targetQuantity < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumQuantity),
                "Replenishment quantities cannot be negative.");
        }

        if (maximumQuantity < minimumQuantity)
        {
            throw new ArgumentException(
                "Maximum quantity must be greater than or equal to minimum quantity.",
                nameof(maximumQuantity));
        }

        if (reorderPointQuantity < safetyStockQuantity)
        {
            throw new ArgumentException(
                "Reorder point must be greater than or equal to safety stock.",
                nameof(reorderPointQuantity));
        }

        if (targetQuantity < reorderPointQuantity || targetQuantity > maximumQuantity)
        {
            throw new ArgumentException(
                "Target quantity must be between the reorder point and maximum quantity.",
                nameof(targetQuantity));
        }
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
    }

}
