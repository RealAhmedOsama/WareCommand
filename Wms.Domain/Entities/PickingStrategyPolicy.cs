using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PickingStrategyPolicy : Entity
{
    private PickingStrategyPolicy()
    {
    }

    public PickingStrategyPolicy(
        int warehouseId,
        string policyKey,
        string name,
        PickingStrategyKind strategy,
        int priority = 50,
        int maxOrders = 100,
        int maxContainers = 100,
        decimal? maxWeightKg = null,
        decimal? maxVolumeCubicMeters = null,
        int? waveTemplateId = null,
        string? orderProfileCode = null,
        int? itemId = null,
        int? locationZoneId = null,
        string? packageProfileCode = null,
        string? sequenceMode = null,
        DateTime? effectiveFromUtc = null,
        DateTime? effectiveToUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ValidateStrategy(strategy);
        PolicyKey = RequiredUpper(policyKey, 80, nameof(policyKey));
        Name = Required(name, 200, nameof(name));
        Priority = ValidateRange(priority, 0, 999, nameof(priority));
        MaxOrders = ValidatePositive(maxOrders, 100_000, nameof(maxOrders));
        MaxContainers = ValidatePositive(maxContainers, 100_000, nameof(maxContainers));
        MaxWeightKg = ValidateNonNegative(maxWeightKg, nameof(maxWeightKg));
        MaxVolumeCubicMeters = ValidateNonNegative(maxVolumeCubicMeters, nameof(maxVolumeCubicMeters));
        ValidateOptionalId(waveTemplateId, nameof(waveTemplateId));
        ValidateOptionalId(itemId, nameof(itemId));
        ValidateOptionalId(locationZoneId, nameof(locationZoneId));
        WarehouseId = warehouseId;
        Strategy = strategy;
        WaveTemplateId = waveTemplateId;
        OrderProfileCode = OptionalUpper(orderProfileCode, 80);
        ItemId = itemId;
        LocationZoneId = locationZoneId;
        PackageProfileCode = OptionalUpper(packageProfileCode, 80);
        SequenceMode = OptionalUpper(sequenceMode, 50) ?? "LOCATION_ITEM";
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc);
        EffectiveToUtc = NormalizeUtc(effectiveToUtc);
        if (EffectiveFromUtc.HasValue && EffectiveToUtc.HasValue && EffectiveToUtc < EffectiveFromUtc)
        {
            throw new ArgumentException("The policy end must be on or after its start.", nameof(effectiveToUtc));
        }

        IsActive = true;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string PolicyKey { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PickingStrategyKind Strategy { get; private set; }
    public int Priority { get; private set; }
    public int MaxOrders { get; private set; }
    public int MaxContainers { get; private set; }
    public decimal? MaxWeightKg { get; private set; }
    public decimal? MaxVolumeCubicMeters { get; private set; }
    public int? WaveTemplateId { get; private set; }
    public string? OrderProfileCode { get; private set; }
    public int? ItemId { get; private set; }
    public int? LocationZoneId { get; private set; }
    public string? PackageProfileCode { get; private set; }
    public string SequenceMode { get; private set; } = "LOCATION_ITEM";
    public DateTime? EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public WaveTemplate? WaveTemplate { get; private set; }
    public Item? Item { get; private set; }
    public Location? LocationZone { get; private set; }

    public void Update(
        string name,
        PickingStrategyKind strategy,
        int priority,
        int maxOrders,
        int maxContainers,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        int? waveTemplateId,
        string? orderProfileCode,
        int? itemId,
        int? locationZoneId,
        string? packageProfileCode,
        string? sequenceMode,
        DateTime? effectiveFromUtc,
        DateTime? effectiveToUtc)
    {
        ValidateStrategy(strategy);
        Name = Required(name, 200, nameof(name));
        Priority = ValidateRange(priority, 0, 999, nameof(priority));
        MaxOrders = ValidatePositive(maxOrders, 100_000, nameof(maxOrders));
        MaxContainers = ValidatePositive(maxContainers, 100_000, nameof(maxContainers));
        MaxWeightKg = ValidateNonNegative(maxWeightKg, nameof(maxWeightKg));
        MaxVolumeCubicMeters = ValidateNonNegative(maxVolumeCubicMeters, nameof(maxVolumeCubicMeters));
        ValidateOptionalId(waveTemplateId, nameof(waveTemplateId));
        ValidateOptionalId(itemId, nameof(itemId));
        ValidateOptionalId(locationZoneId, nameof(locationZoneId));
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc);
        EffectiveToUtc = NormalizeUtc(effectiveToUtc);
        if (EffectiveFromUtc.HasValue && EffectiveToUtc.HasValue && EffectiveToUtc < EffectiveFromUtc)
        {
            throw new ArgumentException("The policy end must be on or after its start.", nameof(effectiveToUtc));
        }

        Strategy = strategy;
        WaveTemplateId = waveTemplateId;
        OrderProfileCode = OptionalUpper(orderProfileCode, 80);
        ItemId = itemId;
        LocationZoneId = locationZoneId;
        PackageProfileCode = OptionalUpper(packageProfileCode, 80);
        SequenceMode = OptionalUpper(sequenceMode, 50) ?? "LOCATION_ITEM";
        Revision++;
        SetUpdatedAt();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Revision++;
        SetUpdatedAt();
    }

    public bool IsEffective(DateTime utcNow) =>
        IsActive &&
        (!EffectiveFromUtc.HasValue || EffectiveFromUtc <= utcNow) &&
        (!EffectiveToUtc.HasValue || EffectiveToUtc >= utcNow);

    private static void ValidateStrategy(PickingStrategyKind strategy)
    {
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }
    }

    private static int ValidateRange(int value, int minimum, int maximum, string parameterName) =>
        value is < 0 or > 999
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

    private static int ValidatePositive(int value, int maximum, string parameterName) =>
        value is < 1 or > 100_000
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

    private static decimal? ValidateNonNegative(decimal? value, string parameterName) =>
        value is < 0m
            ? throw new ArgumentOutOfRangeException(parameterName)
            : value;

    private static void ValidateOptionalId(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
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
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string RequiredUpper(string value, int maximumLength, string parameterName) =>
        Required(value, maximumLength, parameterName).ToUpperInvariant();

    private static string? OptionalUpper(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, maximumLength, nameof(value)).ToUpperInvariant();

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}
