using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Domain.Enums;

namespace Wms.Application.Locations;

public sealed record LocationListQuery(
    int? WarehouseId = null,
    string? SearchTerm = null,
    LocationType? Type = null,
    bool IncludeInactive = true,
    int Page = 1,
    int PageSize = 50);

public sealed record LocationPageDto(
    IReadOnlyList<LocationDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record LocationCreateRequest(
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId,
    LocationType Type,
    string? Barcode,
    int Priority,
    bool IsPickable,
    bool IsReceivable,
    bool IsCountable,
    bool AllowMixedItems,
    bool AllowMixedLots,
    decimal? MaxUnits,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCubicMeters,
    int? MaxPallets,
    int? MaxLpns,
    string? StorageProfile,
    decimal? MinimumTemperatureCelsius,
    decimal? MaximumTemperatureCelsius,
    string? HazardClass,
    string? AccessRestriction,
    string? ConstraintAttributesJson);

public sealed record LocationUpdateRequest(
    int Id,
    string Name,
    int? ParentLocationId,
    LocationType Type,
    string? Barcode,
    int Priority,
    bool IsPickable,
    bool IsReceivable,
    bool IsCountable,
    bool AllowMixedItems,
    bool AllowMixedLots,
    decimal? MaxUnits,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCubicMeters,
    int? MaxPallets,
    int? MaxLpns,
    string? StorageProfile,
    decimal? MinimumTemperatureCelsius,
    decimal? MaximumTemperatureCelsius,
    string? HazardClass,
    string? AccessRestriction,
    string? ConstraintAttributesJson);

public sealed record BulkLocationGenerationRequest(
    int WarehouseId,
    string CodePrefix,
    string NamePrefix,
    int StartNumber,
    int Count,
    int NumberWidth,
    int? ParentLocationId,
    LocationType Type,
    bool IsPickable,
    bool IsReceivable,
    bool IsCountable,
    decimal? MaxUnits,
    string? StorageProfile,
    bool AllowMixedItems = true,
    bool AllowMixedLots = true);

public sealed record LocationImportRow(
    string Code,
    string Name,
    string? ParentCode,
    LocationType Type,
    string? Barcode,
    int Priority,
    bool IsPickable,
    bool IsReceivable,
    bool IsCountable,
    bool AllowMixedItems,
    bool AllowMixedLots,
    decimal? MaxUnits,
    decimal? MaxWeightKg,
    decimal? MaxVolumeCubicMeters,
    int? MaxPallets,
    int? MaxLpns,
    string? StorageProfile);

public sealed record LocationImportError(int Row, string Message);

public sealed record LocationImportResult(
    int CreatedCount,
    IReadOnlyList<LocationImportError> Errors);

public sealed record LocationLabelDto(
    int Id,
    string Code,
    string Name,
    string FullPath,
    string? Barcode,
    LocationType Type,
    int WarehouseId);

public interface ILocationManagementService
{
    Task<Result<LocationPageDto>> ListAsync(
        LocationListQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LocationDto>>> GetChildrenAsync(
        int warehouseId,
        int? parentLocationId,
        int page = 1,
        int pageSize = 100,
        CancellationToken cancellationToken = default);

    Task<Result<LocationDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<LocationDto>> CreateAsync(
        LocationCreateRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<LocationDto>> UpdateAsync(
        LocationUpdateRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> SetActiveAsync(
        int id,
        bool active,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LocationDto>>> GenerateAsync(
        BulkLocationGenerationRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<LocationImportResult>> ImportAsync(
        int warehouseId,
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<LocationLabelDto>>> GetLabelsAsync(
        int warehouseId,
        IReadOnlyCollection<int> locationIds,
        CancellationToken cancellationToken = default);
}
