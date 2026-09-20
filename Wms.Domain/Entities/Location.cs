using System.Globalization;
using System.Text.Json;
using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public class Location : Entity
{
    private const int MaximumCodeLength = 50;
    private const int MaximumNameLength = 200;
    private const int MaximumBarcodeLength = 100;
    private const int MaximumStorageProfileLength = 100;
    private const int MaximumHazardClassLength = 100;
    private const int MaximumAccessRestrictionLength = 200;
    private const int MaximumAttributesJsonLength = 4_000;

    private readonly List<Location> _childLocations = new();

    // EF Constructor
    private Location()
    {
    }

    public Location(
        string code,
        string name,
        int warehouseId,
        int? parentLocationId = null,
        LocationType type = LocationType.Storage,
        string? barcode = null,
        int priority = 0,
        bool isPickable = true,
        bool isReceivable = true,
        bool isCountable = true,
        bool allowMixedItems = true,
        bool allowMixedLots = true,
        decimal? maxUnits = 1_000,
        decimal? maxWeightKg = null,
        decimal? maxVolumeCubicMeters = null,
        int? maxPallets = null,
        int? maxLpns = null,
        string? storageProfile = null,
        decimal? minimumTemperatureCelsius = null,
        decimal? maximumTemperatureCelsius = null,
        string? hazardClass = null,
        string? accessRestriction = null,
        string? constraintAttributesJson = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required", nameof(name));

        // EF-backed aggregate setup can assign the warehouse key after both
        // entities are added to the same unit of work, so zero is retained as
        // a transient value for backwards compatibility. Persisted locations
        // are still protected by the warehouse foreign key.
        ArgumentOutOfRangeException.ThrowIfNegative(warehouseId);

        Code = NormalizeCode(code);
        Name = NormalizeRequired(name, MaximumNameLength, nameof(name));
        WarehouseId = warehouseId;
        ParentLocationId = parentLocationId;
        ApplyDefinition(
            type,
            barcode,
            priority,
            isPickable,
            isReceivable,
            isCountable,
            allowMixedItems,
            allowMixedLots,
            maxUnits,
            maxWeightKg,
            maxVolumeCubicMeters,
            maxPallets,
            maxLpns,
            storageProfile,
            minimumTemperatureCelsius,
            maximumTemperatureCelsius,
            hazardClass,
            accessRestriction,
            constraintAttributesJson,
            stampUpdatedAt: false);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int? ParentLocationId { get; private set; }
    public LocationType Type { get; private set; } = LocationType.Storage;
    public string? Barcode { get; private set; }
    public int Priority { get; private set; }
    public bool IsPickable { get; private set; } = true;
    public bool IsReceivable { get; private set; } = true;
    public bool IsCountable { get; private set; } = true;
    public bool AllowMixedItems { get; private set; } = true;
    public bool AllowMixedLots { get; private set; } = true;
    public bool IsActive { get; private set; } = true;

    // Capacity is retained for compatibility with the original model and UI.
    // MaxUnits is authoritative for new capacity checks; zero means unlimited in
    // the legacy integer projection.
    public int Capacity { get; private set; } = 1_000;
    public decimal? MaxUnits { get; private set; } = 1_000;
    public decimal? MaxWeightKg { get; private set; }
    public decimal? MaxVolumeCubicMeters { get; private set; }
    public int? MaxPallets { get; private set; }
    public int? MaxLpns { get; private set; }
    public string StorageProfile { get; private set; } = string.Empty;
    public decimal? MinimumTemperatureCelsius { get; private set; }
    public decimal? MaximumTemperatureCelsius { get; private set; }
    public string HazardClass { get; private set; } = string.Empty;
    public string AccessRestriction { get; private set; } = string.Empty;
    public string ConstraintAttributesJson { get; private set; } = "{}";

    // Navigation properties
    public Warehouse Warehouse { get; private set; } = null!;
    public Location? ParentLocation { get; private set; }
    public IReadOnlyList<Location> ChildLocations => _childLocations.AsReadOnly();

    public void UpdateDetails(string name)
    {
        Name = NormalizeRequired(name, MaximumNameLength, nameof(name));
        SetUpdatedAt();
    }

    public void UpdateDefinition(
        LocationType type,
        string? barcode,
        int priority,
        bool isPickable,
        bool isReceivable,
        bool isCountable,
        bool allowMixedItems,
        bool allowMixedLots,
        decimal? maxUnits,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        int? maxPallets,
        int? maxLpns,
        string? storageProfile,
        decimal? minimumTemperatureCelsius,
        decimal? maximumTemperatureCelsius,
        string? hazardClass,
        string? accessRestriction,
        string? constraintAttributesJson)
    {
        ApplyDefinition(
            type,
            barcode,
            priority,
            isPickable,
            isReceivable,
            isCountable,
            allowMixedItems,
            allowMixedLots,
            maxUnits,
            maxWeightKg,
            maxVolumeCubicMeters,
            maxPallets,
            maxLpns,
            storageProfile,
            minimumTemperatureCelsius,
            maximumTemperatureCelsius,
            hazardClass,
            accessRestriction,
            constraintAttributesJson,
            stampUpdatedAt: true);
    }

    public void SetParentLocation(int? parentLocationId)
    {
        if (parentLocationId == Id && Id != 0)
            throw new InvalidOperationException("A location cannot be its own parent.");

        ParentLocationId = parentLocationId;
        SetUpdatedAt();
    }

    public void SetCapacity(int capacity)
    {
        if (capacity < 0)
            throw new ArgumentException("Capacity cannot be negative", nameof(capacity));

        SetCapacityDimensions(
            capacity == 0 ? null : capacity,
            MaxWeightKg,
            MaxVolumeCubicMeters,
            MaxPallets,
            MaxLpns);
    }

    public void SetCapacityDimensions(
        decimal? maxUnits,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        int? maxPallets,
        int? maxLpns)
    {
        ValidateCapacity(maxUnits, maxWeightKg, maxVolumeCubicMeters, maxPallets, maxLpns);
        MaxUnits = maxUnits;
        MaxWeightKg = maxWeightKg;
        MaxVolumeCubicMeters = maxVolumeCubicMeters;
        MaxPallets = maxPallets;
        MaxLpns = maxLpns;
        Capacity = maxUnits.HasValue &&
                   maxUnits.Value <= int.MaxValue &&
                   decimal.Truncate(maxUnits.Value) == maxUnits.Value
            ? (int)maxUnits.Value
            : 0;
        SetUpdatedAt();
    }

    public void SetPickable(bool isPickable)
    {
        ValidateWorkflowPolicy(Type, isPickable, IsReceivable);
        IsPickable = isPickable;
        SetUpdatedAt();
    }

    public void SetReceivable(bool isReceivable)
    {
        ValidateWorkflowPolicy(Type, IsPickable, isReceivable);
        IsReceivable = isReceivable;
        SetUpdatedAt();
    }

    public void SetCountable(bool isCountable)
    {
        IsCountable = isCountable;
        SetUpdatedAt();
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

    public bool SupportsPicking => SupportsPickingType(Type);
    public bool SupportsReceiving => SupportsReceivingType(Type);

    public LocationCapacityViolation? ValidateCapacity(
        LocationCapacitySnapshot current,
        LocationCapacitySnapshot incoming)
    {
        if (current.Units + incoming.Units < 0)
        {
            return new LocationCapacityViolation(
                "location.units_invalid",
                "Location units cannot become negative.");
        }

        if (MaxUnits.HasValue && current.Units + incoming.Units > MaxUnits.Value)
        {
            return new LocationCapacityViolation(
                "location.capacity_units_exceeded",
                $"Location capacity is {MaxUnits.Value.ToString("0.####", CultureInfo.InvariantCulture)} units.");
        }

        if (MaxWeightKg.HasValue && current.WeightKg + incoming.WeightKg > MaxWeightKg.Value)
        {
            return new LocationCapacityViolation(
                "location.capacity_weight_exceeded",
                $"Location weight capacity is {MaxWeightKg.Value.ToString("0.####", CultureInfo.InvariantCulture)} kg.");
        }

        if (MaxVolumeCubicMeters.HasValue &&
            current.VolumeCubicMeters + incoming.VolumeCubicMeters > MaxVolumeCubicMeters.Value)
        {
            return new LocationCapacityViolation(
                "location.capacity_volume_exceeded",
                $"Location volume capacity is {MaxVolumeCubicMeters.Value.ToString("0.####", CultureInfo.InvariantCulture)} m³.");
        }

        if (MaxPallets.HasValue && current.Pallets + incoming.Pallets > MaxPallets.Value)
        {
            return new LocationCapacityViolation(
                "location.capacity_pallets_exceeded",
                $"Location pallet capacity is {MaxPallets.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (MaxLpns.HasValue && current.Lpns + incoming.Lpns > MaxLpns.Value)
        {
            return new LocationCapacityViolation(
                "location.capacity_lpns_exceeded",
                $"Location LPN capacity is {MaxLpns.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        return null;
    }

    public string GetFullPath()
    {
        var segments = new List<string>();
        var visited = new HashSet<int>();
        Location? current = this;
        for (var depth = 0; current is not null && depth <= 64; depth++)
        {
            if (current.Id != 0 && !visited.Add(current.Id))
            {
                break;
            }

            segments.Add(current.Code);
            current = current.ParentLocation;
        }

        segments.Reverse();
        return string.Join('.', segments);
    }

    private void ApplyDefinition(
        LocationType type,
        string? barcode,
        int priority,
        bool isPickable,
        bool isReceivable,
        bool isCountable,
        bool allowMixedItems,
        bool allowMixedLots,
        decimal? maxUnits,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        int? maxPallets,
        int? maxLpns,
        string? storageProfile,
        decimal? minimumTemperatureCelsius,
        decimal? maximumTemperatureCelsius,
        string? hazardClass,
        string? accessRestriction,
        string? constraintAttributesJson,
        bool stampUpdatedAt)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        ArgumentOutOfRangeException.ThrowIfNegative(priority);

        ValidateWorkflowPolicy(type, isPickable, isReceivable);
        ValidateCapacity(maxUnits, maxWeightKg, maxVolumeCubicMeters, maxPallets, maxLpns);
        if (minimumTemperatureCelsius.HasValue &&
            maximumTemperatureCelsius.HasValue &&
            minimumTemperatureCelsius > maximumTemperatureCelsius)
        {
            throw new ArgumentException(
                "Minimum temperature cannot be greater than maximum temperature.",
                nameof(minimumTemperatureCelsius));
        }

        Type = type;
        Barcode = NormalizeOptional(barcode, MaximumBarcodeLength);
        Priority = priority;
        IsPickable = isPickable;
        IsReceivable = isReceivable;
        IsCountable = isCountable;
        AllowMixedItems = allowMixedItems;
        AllowMixedLots = allowMixedLots;
        MaxUnits = maxUnits;
        MaxWeightKg = maxWeightKg;
        MaxVolumeCubicMeters = maxVolumeCubicMeters;
        MaxPallets = maxPallets;
        MaxLpns = maxLpns;
        Capacity = maxUnits.HasValue &&
                   maxUnits.Value <= int.MaxValue &&
                   decimal.Truncate(maxUnits.Value) == maxUnits.Value
            ? (int)maxUnits.Value
            : 0;
        StorageProfile = NormalizeOptional(storageProfile, MaximumStorageProfileLength) ?? string.Empty;
        MinimumTemperatureCelsius = minimumTemperatureCelsius;
        MaximumTemperatureCelsius = maximumTemperatureCelsius;
        HazardClass = NormalizeOptional(hazardClass, MaximumHazardClassLength) ?? string.Empty;
        AccessRestriction = NormalizeOptional(accessRestriction, MaximumAccessRestrictionLength) ?? string.Empty;
        ConstraintAttributesJson = NormalizeOptional(
                constraintAttributesJson,
                MaximumAttributesJsonLength) ?? "{}";
        ValidateConstraintAttributes(ConstraintAttributesJson);

        if (stampUpdatedAt)
            SetUpdatedAt();
    }

    private static string NormalizeCode(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > MaximumCodeLength)
            throw new ArgumentException($"Code cannot exceed {MaximumCodeLength} characters", nameof(value));
        return normalized;
    }

    private static void ValidateConstraintAttributes(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Constraint attributes must be a JSON object.",
                    nameof(json));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Constraint attributes must contain valid JSON.",
                nameof(json),
                exception);
        }
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("Value is required", parameterName);
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters", parameterName);
        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters");
        return normalized;
    }

    private static void ValidateCapacity(
        decimal? maxUnits,
        decimal? maxWeightKg,
        decimal? maxVolumeCubicMeters,
        int? maxPallets,
        int? maxLpns)
    {
        if (maxUnits is < 0 || maxWeightKg is < 0 || maxVolumeCubicMeters is < 0 ||
            maxPallets is < 0 || maxLpns is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUnits), "Capacity values cannot be negative.");
        }

    }

    private static void ValidateWorkflowPolicy(
        LocationType type,
        bool isPickable,
        bool isReceivable)
    {
        if (isPickable && !SupportsPickingType(type))
        {
            throw new ArgumentException(
                $"Location type '{type}' cannot be configured as pickable.",
                nameof(isPickable));
        }

        if (isReceivable && !SupportsReceivingType(type))
        {
            throw new ArgumentException(
                $"Location type '{type}' cannot be configured as receivable.",
                nameof(isReceivable));
        }
    }

    private static bool SupportsPickingType(LocationType type) =>
        type is LocationType.Storage or
            LocationType.Zone or
            LocationType.Aisle or
            LocationType.Rack or
            LocationType.Bin or
            LocationType.PickFace or
            LocationType.Bulk;

    private static bool SupportsReceivingType(LocationType type) =>
        type is LocationType.Dock or
            LocationType.Receiving or
            LocationType.Staging or
            LocationType.Storage or
            LocationType.Bulk or
            LocationType.Returns or
            LocationType.Transit;
}
