// Wms.Application/DTOs/LocationDto.cs

using Wms.Domain.Enums;

namespace Wms.Application.DTOs;

public record LocationDto(
    int Id,
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId,
    bool IsPickable,
    bool IsReceivable,
    bool IsActive,
    int Capacity,
    string FullPath,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    LocationType Type = LocationType.Storage,
    string? Barcode = null,
    int Priority = 0,
    bool IsCountable = true,
    bool AllowMixedItems = true,
    bool AllowMixedLots = true,
    decimal? MaxUnits = null,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    int? MaxPallets = null,
    int? MaxLpns = null,
    string StorageProfile = "",
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null,
    string HazardClass = "",
    string AccessRestriction = "",
    string ConstraintAttributesJson = "{}"
);

public record CreateLocationDto(
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId = null,
    bool IsPickable = true,
    bool IsReceivable = true,
    int Capacity = 1000,
    LocationType Type = LocationType.Storage,
    string? Barcode = null,
    int Priority = 0,
    bool IsCountable = true,
    bool AllowMixedItems = true,
    bool AllowMixedLots = true,
    decimal? MaxUnits = 1000,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    int? MaxPallets = null,
    int? MaxLpns = null,
    string? StorageProfile = null,
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null,
    string? HazardClass = null,
    string? AccessRestriction = null,
    string? ConstraintAttributesJson = null
);

public record UpdateLocationDto(
    string Name,
    bool IsPickable = true,
    bool IsReceivable = true,
    int Capacity = 1000,
    LocationType Type = LocationType.Storage,
    string? Barcode = null,
    int Priority = 0,
    bool IsCountable = true,
    bool AllowMixedItems = true,
    bool AllowMixedLots = true,
    decimal? MaxUnits = 1000,
    decimal? MaxWeightKg = null,
    decimal? MaxVolumeCubicMeters = null,
    int? MaxPallets = null,
    int? MaxLpns = null,
    string? StorageProfile = null,
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null,
    string? HazardClass = null,
    string? AccessRestriction = null,
    string? ConstraintAttributesJson = null
);
