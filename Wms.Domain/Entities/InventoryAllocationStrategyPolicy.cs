using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Effective-dated warehouse allocation policy. More specific item/category/
/// demand-type policies can overlap a warehouse default; the resolver chooses
/// the most specific matching policy deterministically.
/// </summary>
public sealed class InventoryAllocationStrategyPolicy : Entity
{
    private const int MaximumPolicyKeyLength = 80;
    private const int MaximumCategoryLength = 100;
    private const int MaximumDemandTypeLength = 100;

    private InventoryAllocationStrategyPolicy()
    {
    }

    public InventoryAllocationStrategyPolicy(
        int warehouseId,
        string policyKey,
        int? itemId,
        string? itemCategory,
        string? demandType,
        InventoryAllocationStrategyKind strategy,
        int? fixedLocationId,
        bool preferWholeLicensePlate,
        int minimumShelfLifeDays,
        InventoryAllocationMissingExpiryFallback missingExpiryFallback,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null)
    {
        ValidateIdentity(warehouseId, policyKey);
        Apply(
            itemId,
            itemCategory,
            demandType,
            strategy,
            fixedLocationId,
            preferWholeLicensePlate,
            minimumShelfLifeDays,
            missingExpiryFallback,
            effectiveFromUtc,
            effectiveToUtc,
            stampUpdatedAt: false);
        WarehouseId = warehouseId;
        PolicyKey = NormalizeRequired(policyKey, MaximumPolicyKeyLength, nameof(policyKey), uppercase: true);
        IsActive = true;
    }

    public int WarehouseId { get; private set; }
    public string PolicyKey { get; private set; } = string.Empty;
    public int? ItemId { get; private set; }
    public string? ItemCategory { get; private set; }
    public string? DemandType { get; private set; }
    public InventoryAllocationStrategyKind Strategy { get; private set; }
    public int? FixedLocationId { get; private set; }
    public bool PreferWholeLicensePlate { get; private set; }
    public int MinimumShelfLifeDays { get; private set; }
    public InventoryAllocationMissingExpiryFallback MissingExpiryFallback { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item? Item { get; private set; }
    public Location? FixedLocation { get; private set; }

    public bool IsEffectiveAt(DateTime utcNow) =>
        IsActive &&
        EffectiveFromUtc <= utcNow &&
        (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > utcNow);

    public void Update(
        string policyKey,
        int? itemId,
        string? itemCategory,
        string? demandType,
        InventoryAllocationStrategyKind strategy,
        int? fixedLocationId,
        bool preferWholeLicensePlate,
        int minimumShelfLifeDays,
        InventoryAllocationMissingExpiryFallback missingExpiryFallback,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null)
    {
        Apply(
            itemId,
            itemCategory,
            demandType,
            strategy,
            fixedLocationId,
            preferWholeLicensePlate,
            minimumShelfLifeDays,
            missingExpiryFallback,
            effectiveFromUtc,
            effectiveToUtc,
            stampUpdatedAt: true);
        PolicyKey = NormalizeRequired(policyKey, MaximumPolicyKeyLength, nameof(policyKey), uppercase: true);
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
        int? itemId,
        string? itemCategory,
        string? demandType,
        InventoryAllocationStrategyKind strategy,
        int? fixedLocationId,
        bool preferWholeLicensePlate,
        int minimumShelfLifeDays,
        InventoryAllocationMissingExpiryFallback missingExpiryFallback,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        bool stampUpdatedAt)
    {
        if (itemId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemId));
        }

        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        if (strategy == InventoryAllocationStrategyKind.FixedLocation && fixedLocationId is not > 0)
        {
            throw new ArgumentException(
                "A fixed-location strategy requires a fixed location.",
                nameof(fixedLocationId));
        }

        if (fixedLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedLocationId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minimumShelfLifeDays);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumShelfLifeDays, 3_650);

        if (!Enum.IsDefined(missingExpiryFallback))
        {
            throw new ArgumentOutOfRangeException(nameof(missingExpiryFallback));
        }

        var normalizedFrom = NormalizeUtc(effectiveFromUtc);
        DateTime? normalizedTo = effectiveToUtc.HasValue
            ? NormalizeUtc(effectiveToUtc.Value)
            : null;
        if (normalizedTo.HasValue && normalizedTo.Value <= normalizedFrom)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        ItemId = itemId;
        ItemCategory = NormalizeOptional(itemCategory, MaximumCategoryLength, uppercase: true);
        DemandType = NormalizeOptional(demandType, MaximumDemandTypeLength, uppercase: true);
        Strategy = strategy;
        FixedLocationId = fixedLocationId;
        PreferWholeLicensePlate = preferWholeLicensePlate;
        MinimumShelfLifeDays = minimumShelfLifeDays;
        MissingExpiryFallback = missingExpiryFallback;
        EffectiveFromUtc = normalizedFrom;
        EffectiveToUtc = normalizedTo;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static void ValidateIdentity(int warehouseId, string policyKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        _ = NormalizeRequired(policyKey, MaximumPolicyKeyLength, nameof(policyKey), uppercase: true);
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

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        bool uppercase = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (uppercase)
        {
            normalized = normalized.ToUpperInvariant();
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
    }
}
