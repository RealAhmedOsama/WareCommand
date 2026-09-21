using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PutawayRule : Entity
{
    private PutawayRule()
    {
    }

    public PutawayRule(
        int warehouseId,
        string code,
        string name,
        PutawayRuleStrategy strategy,
        int priority,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        int? itemId = null,
        string? itemCategory = null,
        int? supplierId = null,
        string? packageType = null,
        LicensePlateType? licensePlateType = null,
        int? inventoryStatusId = null,
        LotStatus? lotStatus = null,
        decimal? minimumTemperatureCelsius = null,
        decimal? maximumTemperatureCelsius = null,
        string? hazardClass = null,
        string? storageProfile = null,
        string? sourceProcess = null,
        int? fixedLocationId = null,
        LocationType? targetLocationType = null,
        string? targetStorageProfile = null,
        int? fallbackLocationId = null,
        bool isSimulation = false,
        bool isActive = true,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        Code = Required(code, 80, nameof(code)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        WarehouseId = warehouseId;
        Strategy = strategy;
        Priority = priority;
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc);
        EffectiveToUtc = effectiveToUtc.HasValue ? NormalizeUtc(effectiveToUtc.Value) : null;
        EnsureEffectiveRange();
        ItemId = PositiveOrNull(itemId, nameof(itemId));
        ItemCategory = Optional(itemCategory, 100)?.ToUpperInvariant();
        SupplierId = PositiveOrNull(supplierId, nameof(supplierId));
        PackageType = Optional(packageType, 50)?.ToUpperInvariant();
        LicensePlateType = licensePlateType;
        InventoryStatusId = PositiveOrNull(inventoryStatusId, nameof(inventoryStatusId));
        LotStatus = lotStatus;
        MinimumTemperatureCelsius = minimumTemperatureCelsius;
        MaximumTemperatureCelsius = maximumTemperatureCelsius;
        if (MinimumTemperatureCelsius.HasValue && MaximumTemperatureCelsius.HasValue &&
            MinimumTemperatureCelsius > MaximumTemperatureCelsius)
        {
            throw new ArgumentException(
                "Minimum temperature cannot be greater than maximum temperature.",
                nameof(minimumTemperatureCelsius));
        }

        HazardClass = Optional(hazardClass, 100)?.ToUpperInvariant();
        StorageProfile = Optional(storageProfile, 100)?.ToUpperInvariant();
        SourceProcess = Optional(sourceProcess, 50)?.ToUpperInvariant();
        FixedLocationId = PositiveOrNull(fixedLocationId, nameof(fixedLocationId));
        TargetLocationType = targetLocationType;
        TargetStorageProfile = Optional(targetStorageProfile, 100)?.ToUpperInvariant();
        FallbackLocationId = PositiveOrNull(fallbackLocationId, nameof(fallbackLocationId));
        IsSimulation = isSimulation;
        IsActive = isActive;
        Notes = Optional(notes, 2_000);
        ValidateStrategyRequirements();
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public PutawayRuleStrategy Strategy { get; private set; }
    public int Priority { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public int? ItemId { get; private set; }
    public string? ItemCategory { get; private set; }
    public int? SupplierId { get; private set; }
    public string? PackageType { get; private set; }
    public LicensePlateType? LicensePlateType { get; private set; }
    public int? InventoryStatusId { get; private set; }
    public LotStatus? LotStatus { get; private set; }
    public decimal? MinimumTemperatureCelsius { get; private set; }
    public decimal? MaximumTemperatureCelsius { get; private set; }
    public string? HazardClass { get; private set; }
    public string? StorageProfile { get; private set; }
    public string? SourceProcess { get; private set; }
    public int? FixedLocationId { get; private set; }
    public LocationType? TargetLocationType { get; private set; }
    public string? TargetStorageProfile { get; private set; }
    public int? FallbackLocationId { get; private set; }
    public bool IsSimulation { get; private set; }
    public bool IsActive { get; private set; }
    public string? Notes { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item? Item { get; private set; }
    public Supplier? Supplier { get; private set; }
    public Location? FixedLocation { get; private set; }
    public Location? FallbackLocation { get; private set; }

    public void Update(
        string name,
        PutawayRuleStrategy strategy,
        int priority,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        int? itemId,
        string? itemCategory,
        int? supplierId,
        string? packageType,
        LicensePlateType? licensePlateType,
        int? inventoryStatusId,
        LotStatus? lotStatus,
        decimal? minimumTemperatureCelsius,
        decimal? maximumTemperatureCelsius,
        string? hazardClass,
        string? storageProfile,
        string? sourceProcess,
        int? fixedLocationId,
        LocationType? targetLocationType,
        string? targetStorageProfile,
        int? fallbackLocationId,
        bool isSimulation,
        string? notes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        Name = Required(name, 200, nameof(name));
        Strategy = strategy;
        Priority = priority;
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc);
        EffectiveToUtc = effectiveToUtc.HasValue ? NormalizeUtc(effectiveToUtc.Value) : null;
        EnsureEffectiveRange();
        ItemId = PositiveOrNull(itemId, nameof(itemId));
        ItemCategory = Optional(itemCategory, 100)?.ToUpperInvariant();
        SupplierId = PositiveOrNull(supplierId, nameof(supplierId));
        PackageType = Optional(packageType, 50)?.ToUpperInvariant();
        LicensePlateType = licensePlateType;
        InventoryStatusId = PositiveOrNull(inventoryStatusId, nameof(inventoryStatusId));
        LotStatus = lotStatus;
        MinimumTemperatureCelsius = minimumTemperatureCelsius;
        MaximumTemperatureCelsius = maximumTemperatureCelsius;
        if (MinimumTemperatureCelsius.HasValue && MaximumTemperatureCelsius.HasValue &&
            MinimumTemperatureCelsius > MaximumTemperatureCelsius)
        {
            throw new ArgumentException(
                "Minimum temperature cannot be greater than maximum temperature.",
                nameof(minimumTemperatureCelsius));
        }

        HazardClass = Optional(hazardClass, 100)?.ToUpperInvariant();
        StorageProfile = Optional(storageProfile, 100)?.ToUpperInvariant();
        SourceProcess = Optional(sourceProcess, 50)?.ToUpperInvariant();
        FixedLocationId = PositiveOrNull(fixedLocationId, nameof(fixedLocationId));
        TargetLocationType = targetLocationType;
        TargetStorageProfile = Optional(targetStorageProfile, 100)?.ToUpperInvariant();
        FallbackLocationId = PositiveOrNull(fallbackLocationId, nameof(fallbackLocationId));
        IsSimulation = isSimulation;
        Notes = Optional(notes, 2_000);
        ValidateStrategyRequirements();
        Touch();
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        Touch();
    }

    private void ValidateStrategyRequirements()
    {
        if (Strategy == PutawayRuleStrategy.FixedLocation && !FixedLocationId.HasValue)
        {
            throw new ArgumentException(
                "A fixed-location rule requires a fixed location.",
                nameof(FixedLocationId));
        }
    }

    private void EnsureEffectiveRange()
    {
        if (EffectiveToUtc.HasValue && EffectiveToUtc <= EffectiveFromUtc)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(EffectiveToUtc));
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static int? PositiveOrNull(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
    }

}
