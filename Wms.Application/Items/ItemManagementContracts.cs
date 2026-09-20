using Wms.Application.Common;
using Wms.Application.DTOs;
using Wms.Domain.Enums;

namespace Wms.Application.Items;

public enum ItemSortField
{
    Sku,
    Name,
    Category,
    UpdatedAt,
    CreatedAt
}

public sealed record ItemListQuery(
    string? SearchTerm = null,
    ItemType? Type = null,
    ItemLifecycleStatus? LifecycleStatus = null,
    string? Category = null,
    bool IncludeInactive = false,
    ItemSortField SortBy = ItemSortField.Sku,
    bool Descending = false,
    int Page = 1,
    int PageSize = 50);

public sealed record ItemPageDto(
    IReadOnlyList<ItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ItemCommercialRequest(
    string Name,
    string? LocalizedName = null,
    string? Description = null,
    string? LocalizedDescription = null,
    string? Category = null,
    string? Brand = null,
    ItemType Type = ItemType.Stock,
    ItemLifecycleStatus LifecycleStatus = ItemLifecycleStatus.Active,
    string? ImageReference = null,
    string? DocumentReference = null);

public sealed record ItemMeasurementRequest(
    string BaseUnit,
    string? PurchaseUnit = null,
    string? SalesUnit = null,
    decimal? NetWeightKg = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    decimal? VolumeCubicMeters = null,
    decimal? StandardCost = null,
    decimal? SalesPrice = null,
    string? CountryOfOrigin = null,
    string? CustomsCode = null);

public sealed record ItemTrackingRequest(
    bool RequiresLot = false,
    bool RequiresSerial = false,
    bool RequiresExpiry = false,
    int ShelfLifeDays = 0,
    bool UseFefo = false,
    bool QualityInspectionRequired = false,
    bool IsHazardous = false,
    bool TemperatureControlled = false,
    bool SpecialHandlingRequired = false,
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null);

public sealed record ItemStorageRequest(
    string? StorageProfile = null,
    string? PutawayProfile = null,
    string? DefaultSupplierCode = null);

public sealed record ItemPlanningRequest(
    ItemReorderPolicy ReorderPolicy = ItemReorderPolicy.None,
    decimal? MinimumStock = null,
    decimal? MaximumStock = null,
    decimal? SafetyStock = null,
    int? LeadTimeDays = null);

public sealed record ItemPackagingRequest(
    string Code,
    string UnitOfMeasure,
    decimal UnitsPerPackage,
    string? Barcode = null,
    decimal? GrossWeightKg = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    bool IsDefault = false);

public sealed record ItemCreateRequest(
    string Sku,
    ItemCommercialRequest Commercial,
    ItemMeasurementRequest Measurements,
    ItemTrackingRequest Tracking,
    ItemStorageRequest Storage,
    ItemPlanningRequest Planning,
    IReadOnlyList<string>? Barcodes = null,
    IReadOnlyList<ItemPackagingRequest>? Packagings = null);

public sealed record ItemUpdateRequest(
    int Id,
    ItemCommercialRequest Commercial,
    ItemMeasurementRequest Measurements,
    ItemTrackingRequest Tracking,
    ItemStorageRequest Storage,
    ItemPlanningRequest Planning,
    IReadOnlyList<string>? Barcodes = null,
    IReadOnlyList<ItemPackagingRequest>? Packagings = null);

public sealed record ItemDuplicateRequest(
    int SourceItemId,
    string Sku,
    string? Name = null,
    bool CopyPackagings = true);

public sealed record ItemImportError(int Row, string Message);

public sealed record ItemImportResult(
    int ImportedCount,
    IReadOnlyList<ItemImportError> Errors);

public interface IItemManagementService
{
    Task<Result<ItemPageDto>> ListAsync(
        ItemListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<ItemDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<ItemDto>> CreateAsync(
        ItemCreateRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ItemDto>> UpdateAsync(
        ItemUpdateRequest request,
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

    Task<Result<ItemDto>> DuplicateAsync(
        ItemDuplicateRequest request,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ItemImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        ItemListQuery request,
        CancellationToken cancellationToken = default);
}
