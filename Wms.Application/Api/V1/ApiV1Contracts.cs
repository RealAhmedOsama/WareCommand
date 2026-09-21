namespace Wms.Application.Api.V1;

public static class WmsApiV1
{
    public const string Version = "v1";
    public const string Prefix = "/api/v1";
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
    public const string OpenApiPath = "/api/v1/openapi.json";

    public static readonly IReadOnlyList<string> QueryResources =
    [
        "warehouses",
        "items",
        "locations",
        "inventory/stock",
        "reports/movements"
    ];

    public static readonly IReadOnlyList<string> CommandResources = [];
}

public sealed record ApiPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ApiVersionInfo(
    string Version,
    string Status,
    string OpenApi,
    string Authentication,
    string DeprecationPolicy,
    IReadOnlyList<string> QueryResources,
    IReadOnlyList<string> CommandResources);

public sealed record ApiWarehouse(
    int Id,
    string Code,
    string Name,
    string ArabicName,
    bool IsActive,
    bool WorkflowEnabled,
    bool WorkflowReady,
    int LocationCount,
    int ActiveLocationCount,
    int ConfiguredOperationalLocationCount,
    int AssignedUserCount);

public sealed record ApiItem(
    int Id,
    string Sku,
    string Name,
    string LocalizedName,
    string? Category,
    string? Brand,
    string UnitOfMeasure,
    string PurchaseUnit,
    string SalesUnit,
    string Type,
    string LifecycleStatus,
    bool IsActive,
    bool RequiresLot,
    bool RequiresSerial,
    bool RequiresExpiry,
    int ShelfLifeDays,
    IReadOnlyList<string> Barcodes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ApiLocation(
    int Id,
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId,
    string Type,
    string? Barcode,
    bool IsPickable,
    bool IsReceivable,
    bool IsCountable,
    bool IsActive,
    int Capacity,
    string FullPath,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ApiInventoryStock(
    int Id,
    int ItemId,
    string ItemSku,
    string ItemName,
    int LocationId,
    string LocationCode,
    string LocationName,
    int? LotId,
    string? LotNumber,
    string? SerialNumber,
    decimal QuantityAvailable,
    decimal QuantityReserved,
    decimal AvailableQuantity,
    decimal? AvailableToPromiseQuantity,
    decimal? HeldQuantity,
    decimal? InTransitQuantity,
    decimal? OrderedQuantity,
    DateTime? ExpiryDate,
    string InventoryStatusCode,
    string InventoryStatusName,
    bool IsAllocatable,
    bool IsPickable,
    bool IsShippable,
    int? LicensePlateId,
    string? LicensePlateNumber,
    string OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record ApiMovement(
    int Id,
    string Type,
    string ItemSku,
    string ItemName,
    string? FromLocationCode,
    string? ToLocationCode,
    decimal Quantity,
    decimal BaseQuantity,
    string BaseUnitOfMeasure,
    decimal DisplayQuantity,
    string DisplayUnitOfMeasure,
    string? LotNumber,
    string? SerialNumber,
    string UserId,
    string? ReferenceNumber,
    string? Notes,
    DateTime Timestamp,
    string OwnerKind,
    int? InventoryOwnerId,
    string OwnerCodeSnapshot);

public sealed record ApiReportMetadata(
    string ReportName,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset DataCutoffUtc,
    string TimeZoneId,
    string? UserId,
    IReadOnlyDictionary<string, string?> Filters);

public sealed record ApiMovementPage(
    IReadOnlyList<ApiMovement> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    ApiReportMetadata Metadata);

public static class ApiV1Mapping
{
    public static ApiWarehouse ToApi(this Wms.Application.Warehouses.WarehouseSummaryDto source) =>
        new(
            source.Id,
            source.Code,
            source.Name,
            source.ArabicName,
            source.IsActive,
            source.WorkflowEnabled,
            source.WorkflowReady,
            source.LocationCount,
            source.ActiveLocationCount,
            source.ConfiguredOperationalLocationCount,
            source.AssignedUserCount);

    public static ApiItem ToApi(this Wms.Application.DTOs.ItemDto source) =>
        new(
            source.Id,
            source.Sku,
            source.Name,
            source.LocalizedName,
            source.Category,
            source.Brand,
            source.UnitOfMeasure,
            source.PurchaseUnit,
            source.SalesUnit,
            source.Type.ToString(),
            source.LifecycleStatus.ToString(),
            source.IsActive,
            source.RequiresLot,
            source.RequiresSerial,
            source.RequiresExpiry,
            source.ShelfLifeDays,
            source.Barcodes,
            source.CreatedAt,
            source.UpdatedAt);

    public static ApiLocation ToApi(this Wms.Application.DTOs.LocationDto source) =>
        new(
            source.Id,
            source.Code,
            source.Name,
            source.WarehouseId,
            source.ParentLocationId,
            source.Type.ToString(),
            source.Barcode,
            source.IsPickable,
            source.IsReceivable,
            source.IsCountable,
            source.IsActive,
            source.Capacity,
            source.FullPath,
            source.CreatedAt,
            source.UpdatedAt);

    public static ApiInventoryStock ToApi(this Wms.Application.DTOs.StockDto source) =>
        new(
            source.Id,
            source.ItemId,
            source.ItemSku,
            source.ItemName,
            source.LocationId,
            source.LocationCode,
            source.LocationName,
            source.LotId,
            source.LotNumber,
            source.SerialNumber,
            source.QuantityAvailable,
            source.QuantityReserved,
            source.AvailableQuantity,
            source.AvailableToPromiseQuantity,
            source.HeldQuantity,
            source.InTransitQuantity,
            source.OrderedQuantity,
            source.ExpiryDate,
            source.InventoryStatusCode,
            source.InventoryStatusName,
            source.IsAllocatable,
            source.IsPickable,
            source.IsShippable,
            source.LicensePlateId,
            source.LicensePlateNumber,
            source.OwnerKind.ToString(),
            source.InventoryOwnerId,
            source.OwnerCodeSnapshot,
            source.CreatedAt,
            source.UpdatedAt);

    public static ApiMovement ToApi(this Wms.Application.UseCases.Reports.MovementReportDto source) =>
        new(
            source.Id,
            source.Type,
            source.ItemSku,
            source.ItemName,
            source.FromLocationCode,
            source.ToLocationCode,
            source.Quantity,
            source.BaseQuantity,
            source.BaseUnitOfMeasure,
            source.DisplayQuantity,
            source.DisplayUnitOfMeasure,
            source.LotNumber,
            source.SerialNumber,
            source.UserId,
            source.ReferenceNumber,
            source.Notes,
            source.Timestamp,
            source.OwnerKind.ToString(),
            source.InventoryOwnerId,
            source.OwnerCodeSnapshot);

    public static ApiReportMetadata ToApi(this Wms.Application.Reporting.ReportMetadata source) =>
        new(
            source.ReportName,
            source.GeneratedAtUtc,
            source.DataCutoffUtc,
            source.TimeZoneId,
            source.UserId,
            source.Filters);
}
