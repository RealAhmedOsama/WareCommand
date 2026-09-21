using System.Globalization;
using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Effective-dated deterministic slotting weights. Scores rank candidates only
/// after location/item hard constraints have been evaluated.
/// </summary>
public sealed class SlottingPolicy : Entity
{
    private SlottingPolicy()
    {
    }

    public SlottingPolicy(
        int warehouseId,
        string policyKey,
        string name,
        int lookbackDays,
        decimal velocityWeight,
        decimal travelWeight,
        decimal spaceWeight,
        decimal replenishmentWeight,
        decimal affinityWeight,
        int maxRecommendationsPerItem,
        int recommendationExpiryDays,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        string? allowedLocationTypes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        PolicyKey = Required(policyKey, 80, nameof(policyKey)).ToUpperInvariant();
        Apply(
            name,
            lookbackDays,
            velocityWeight,
            travelWeight,
            spaceWeight,
            replenishmentWeight,
            affinityWeight,
            maxRecommendationsPerItem,
            recommendationExpiryDays,
            effectiveFromUtc,
            effectiveToUtc,
            allowedLocationTypes,
            stampUpdatedAt: false);
        WarehouseId = warehouseId;
        IsActive = true;
    }

    public int WarehouseId { get; private set; }
    public string PolicyKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int LookbackDays { get; private set; }
    public decimal VelocityWeight { get; private set; }
    public decimal TravelWeight { get; private set; }
    public decimal SpaceWeight { get; private set; }
    public decimal ReplenishmentWeight { get; private set; }
    public decimal AffinityWeight { get; private set; }
    public int MaxRecommendationsPerItem { get; private set; }
    public int RecommendationExpiryDays { get; private set; }
    public string AllowedLocationTypes { get; private set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;

    public bool IsEffectiveAt(DateTime utcNow) =>
        IsActive &&
        EffectiveFromUtc <= utcNow &&
        (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > utcNow);

    public IReadOnlySet<LocationType> GetAllowedLocationTypes()
    {
        var values = new HashSet<LocationType>();
        foreach (var token in AllowedLocationTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<LocationType>(token, ignoreCase: true, out var type))
            {
                values.Add(type);
            }
        }

        return values;
    }

    public void Update(
        string name,
        int lookbackDays,
        decimal velocityWeight,
        decimal travelWeight,
        decimal spaceWeight,
        decimal replenishmentWeight,
        decimal affinityWeight,
        int maxRecommendationsPerItem,
        int recommendationExpiryDays,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        string? allowedLocationTypes = null)
    {
        Apply(
            name,
            lookbackDays,
            velocityWeight,
            travelWeight,
            spaceWeight,
            replenishmentWeight,
            affinityWeight,
            maxRecommendationsPerItem,
            recommendationExpiryDays,
            effectiveFromUtc,
            effectiveToUtc,
            allowedLocationTypes,
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
        string name,
        int lookbackDays,
        decimal velocityWeight,
        decimal travelWeight,
        decimal spaceWeight,
        decimal replenishmentWeight,
        decimal affinityWeight,
        int maxRecommendationsPerItem,
        int recommendationExpiryDays,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        string? allowedLocationTypes,
        bool stampUpdatedAt)
    {
        Name = Required(name, 200, nameof(name));
        if (lookbackDays is < 1 or > 3_650)
        {
            throw new ArgumentOutOfRangeException(nameof(lookbackDays));
        }

        var weights = new[]
        {
            velocityWeight,
            travelWeight,
            spaceWeight,
            replenishmentWeight,
            affinityWeight
        };
        if (weights.Any(weight => weight < 0m) || weights.Sum() <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(velocityWeight),
                "At least one slotting weight must be positive and no weight may be negative.");
        }

        if (maxRecommendationsPerItem is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecommendationsPerItem));
        }

        if (recommendationExpiryDays is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(recommendationExpiryDays));
        }

        var from = Entity.NormalizeUtc(effectiveFromUtc);
        var to = effectiveToUtc.HasValue ? Entity.NormalizeUtc(effectiveToUtc.Value) : (DateTime?)null;
        if (to.HasValue && to.Value <= from)
        {
            throw new ArgumentException(
                "The policy effective end must be after its effective start.",
                nameof(effectiveToUtc));
        }

        var types = string.IsNullOrWhiteSpace(allowedLocationTypes)
            ? "PickFace,Bin,Rack,Bulk"
            : string.Join(',', allowedLocationTypes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => Enum.TryParse<LocationType>(value, true, out var type)
                    ? type.ToString()
                    : throw new ArgumentException(
                        $"Unknown slotting location type '{value}'.",
                        nameof(allowedLocationTypes)))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(types))
        {
            throw new ArgumentException("At least one candidate location type is required.", nameof(allowedLocationTypes));
        }

        LookbackDays = lookbackDays;
        VelocityWeight = velocityWeight;
        TravelWeight = travelWeight;
        SpaceWeight = spaceWeight;
        ReplenishmentWeight = replenishmentWeight;
        AffinityWeight = affinityWeight;
        MaxRecommendationsPerItem = maxRecommendationsPerItem;
        RecommendationExpiryDays = recommendationExpiryDays;
        AllowedLocationTypes = types;
        EffectiveFromUtc = from;
        EffectiveToUtc = to;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
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
}
