using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Warehouse-scoped expiry and disposition policy. The policy is evaluated at
/// the business-date boundary and never mutates inventory by itself.
/// </summary>
public sealed class InventoryDispositionPolicy : Entity
{
    private InventoryDispositionPolicy()
    {
    }

    public InventoryDispositionPolicy(
        int warehouseId,
        string policyKey,
        string name,
        int priority,
        int? itemId,
        string? itemCategory,
        int warningDays,
        int minimumShelfLifeDays,
        bool requireApprovalForScrap,
        bool requireWitnessForDestruction,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        bool isActive = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (itemId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        if (priority < 0 || warningDays < 0 || minimumShelfLifeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        WarehouseId = warehouseId;
        PolicyKey = Required(policyKey, 80, nameof(policyKey)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        Priority = priority;
        ItemId = itemId;
        ItemCategory = Optional(itemCategory, 100);
        WarningDays = warningDays;
        MinimumShelfLifeDays = minimumShelfLifeDays;
        RequireApprovalForScrap = requireApprovalForScrap;
        RequireWitnessForDestruction = requireWitnessForDestruction;
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc);
        EffectiveToUtc = NormalizeOptionalUtc(effectiveToUtc);
        if (EffectiveToUtc.HasValue && EffectiveToUtc.Value <= EffectiveFromUtc)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        IsActive = isActive;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string PolicyKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public int? ItemId { get; private set; }
    public string? ItemCategory { get; private set; }
    public int WarningDays { get; private set; }
    public int MinimumShelfLifeDays { get; private set; }
    public bool RequireApprovalForScrap { get; private set; }
    public bool RequireWitnessForDestruction { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item? Item { get; private set; }

    public bool IsEffective(DateTime asOfUtc)
    {
        var instant = NormalizeUtc(asOfUtc);
        return IsActive &&
               EffectiveFromUtc <= instant &&
               (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > instant);
    }

    public bool Matches(int itemId, string? itemCategory) =>
        (!ItemId.HasValue || ItemId.Value == itemId) &&
        (string.IsNullOrWhiteSpace(ItemCategory) ||
         string.Equals(ItemCategory, itemCategory?.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Update(
        string name,
        int priority,
        int? itemId,
        string? itemCategory,
        int warningDays,
        int minimumShelfLifeDays,
        bool requireApprovalForScrap,
        bool requireWitnessForDestruction,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        bool isActive)
    {
        if (itemId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        if (priority < 0 || warningDays < 0 || minimumShelfLifeDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        var start = NormalizeUtc(effectiveFromUtc);
        var end = NormalizeOptionalUtc(effectiveToUtc);
        if (end.HasValue && end.Value <= start)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        Name = Required(name, 200, nameof(name));
        Priority = priority;
        ItemId = itemId;
        ItemCategory = Optional(itemCategory, 100);
        WarningDays = warningDays;
        MinimumShelfLifeDays = minimumShelfLifeDays;
        RequireApprovalForScrap = requireApprovalForScrap;
        RequireWitnessForDestruction = requireWitnessForDestruction;
        EffectiveFromUtc = start;
        EffectiveToUtc = end;
        IsActive = isActive;
        Revision++;
        SetUpdatedAt();
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? Optional(string? value, int maximumLength)
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

    private static DateTime? NormalizeOptionalUtc(DateTime? value) =>
        value.HasValue ? Entity.NormalizeUtc(value.Value) : null;
}
