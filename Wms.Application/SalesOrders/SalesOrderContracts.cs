using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.SalesOrders;

public enum SalesOrderSortField
{
    DocumentNumber,
    CustomerCode,
    Status,
    OrderDate,
    RequestedShipDate,
    Priority,
    UpdatedAt
}

public sealed record SalesOrderListQuery(
    int? WarehouseId = null,
    int? CustomerId = null,
    SalesOrderStatus? Status = null,
    string? SearchTerm = null,
    DateOnly? OrderDateFrom = null,
    DateOnly? OrderDateTo = null,
    bool IncludeCancelled = true,
    SalesOrderSortField SortBy = SalesOrderSortField.DocumentNumber,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

public sealed record SalesOrderLineInput(
    string ItemSku,
    decimal OrderedQuantity,
    string? UnitOfMeasure = null,
    string? PackagingCode = null,
    string? CustomerItemSku = null,
    string? Notes = null);

public sealed record SalesOrderInput(
    int WarehouseId,
    int CustomerId,
    int? ShipToAddressId = null,
    string? ShipToCode = null,
    DateOnly? OrderDate = null,
    DateOnly? RequestedShipDate = null,
    string? ExternalReference = null,
    string SourceType = "MANUAL",
    string? SourceReference = null,
    int? Priority = null,
    string? DefaultCarrierCode = null,
    string? DefaultCarrierServiceCode = null,
    bool? AllowPartialShipment = null,
    string? PackagingProfile = null,
    string? LabelProfile = null,
    string? Notes = null,
    IReadOnlyList<SalesOrderLineInput>? Lines = null);

public sealed record SalesOrderLineDto(
    int Id,
    int LineNumber,
    int ItemId,
    string ItemSku,
    string ItemName,
    string? ItemLocalizedName,
    string? CustomerItemSku,
    string OrderedUnitOfMeasure,
    decimal OrderedQuantity,
    string BaseUnitOfMeasure,
    decimal OrderedBaseQuantity,
    decimal AllocatedBaseQuantity,
    decimal PickedBaseQuantity,
    decimal PackedBaseQuantity,
    decimal ShippedBaseQuantity,
    decimal CancelledBaseQuantity,
    decimal BackorderBaseQuantity,
    string ConversionPath,
    string ConversionRuleIds,
    decimal ConversionFactorToBase,
    int ConversionPrecision,
    string? PackagingCode,
    int? PackagingVersion,
    decimal? PackagingUnitsPerPackage,
    string? Notes,
    long Revision);

public sealed record SalesOrderDto(
    int Id,
    string DocumentNumber,
    int WarehouseId,
    string WarehouseCode,
    int CustomerId,
    string CustomerCode,
    string CustomerLegalName,
    string? CustomerLocalizedName,
    string? CustomerContactName,
    string? CustomerContactEmail,
    string? CustomerContactPhone,
    int? ShipToAddressId,
    string? ShipToCode,
    string? ShipToRecipientName,
    string? ShipToPhone,
    string? ShipToCountryCode,
    string? ShipToRegion,
    string? ShipToCity,
    string? ShipToPostalCode,
    string? ShipToAddressLine1,
    string? ShipToAddressLine2,
    string? ShipToDeliveryInstructions,
    DateOnly OrderDate,
    DateOnly? RequestedShipDate,
    string? ExternalReference,
    string SourceType,
    string? SourceReference,
    int Priority,
    string? DefaultCarrierCode,
    string? DefaultCarrierServiceCode,
    string? PackagingProfile,
    string? LabelProfile,
    bool AllowPartialShipment,
    string? Notes,
    SalesOrderStatus Status,
    string? HoldReason,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? ConfirmedAtUtc,
    DateTime? HeldAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? ClosedAtUtc,
    long Revision,
    IReadOnlyList<SalesOrderLineDto> Lines,
    bool CanEdit,
    bool CanConfirm,
    bool CanCancel,
    bool CanHold,
    bool CanReleaseHold,
    bool CanClose);

public sealed record SalesOrderPageDto(
    IReadOnlyList<SalesOrderDto> SalesOrders,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record SalesOrderImportError(int Row, string Message);

public sealed record SalesOrderImportResult(
    int ImportedCount,
    IReadOnlyList<SalesOrderImportError> Errors);

public interface ISalesOrderService
{
    Task<Result<SalesOrderPageDto>> ListAsync(
        SalesOrderListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> CreateAsync(
        SalesOrderInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> UpdateAsync(
        int id,
        SalesOrderInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> ConfirmAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> HoldAsync(
        int id,
        string reason,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> ReleaseHoldAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderDto>> CloseAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        SalesOrderListQuery request,
        CancellationToken cancellationToken = default);
}
