using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Purchasing;

public enum PurchaseOrderSortField
{
    DocumentNumber,
    SupplierCode,
    Status,
    OrderDate,
    ExpectedReceiptDate,
    UpdatedAt
}

public sealed record PurchaseOrderListQuery(
    int? WarehouseId = null,
    int? SupplierId = null,
    PurchaseOrderStatus? Status = null,
    string? SearchTerm = null,
    DateOnly? OrderDateFrom = null,
    DateOnly? OrderDateTo = null,
    bool IncludeCancelled = true,
    PurchaseOrderSortField SortBy = PurchaseOrderSortField.DocumentNumber,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

public sealed record PurchaseOrderLineInput(
    string ItemSku,
    decimal OrderedQuantity,
    string? UnitOfMeasure = null,
    decimal? OverDeliveryTolerancePercent = null,
    decimal? UnderDeliveryTolerancePercent = null,
    string? SupplierItemReference = null,
    string? Notes = null);

public sealed record PurchaseOrderInput(
    int WarehouseId,
    int SupplierId,
    DateOnly OrderDate,
    DateOnly? ExpectedReceiptDate = null,
    string? ExternalReference = null,
    string SourceType = "MANUAL",
    string? SourceReference = null,
    string? CurrencyCode = null,
    string? Notes = null,
    IReadOnlyList<PurchaseOrderLineInput>? Lines = null);

public sealed record PurchaseOrderLineDto(
    int Id,
    int LineNumber,
    int ItemId,
    string ItemSku,
    string ItemName,
    string OrderedUnitOfMeasure,
    decimal OrderedQuantity,
    string BaseUnitOfMeasure,
    decimal OrderedBaseQuantity,
    decimal ReceivedQuantity,
    decimal ReceivedBaseQuantity,
    decimal RemainingBaseQuantity,
    decimal OverDeliveryTolerancePercent,
    decimal UnderDeliveryTolerancePercent,
    string? SupplierItemReference,
    string? Notes,
    bool IsClosed,
    bool IsFullyReceived,
    string ConversionPath,
    string ConversionRuleIds,
    decimal ConversionFactorToBase,
    int ConversionPrecision);

public sealed record PurchaseOrderDto(
    int Id,
    string DocumentNumber,
    int WarehouseId,
    string WarehouseCode,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    DateOnly OrderDate,
    DateOnly? ExpectedReceiptDate,
    string? ExternalReference,
    string SourceType,
    string? SourceReference,
    string? CurrencyCode,
    string? Notes,
    PurchaseOrderStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? ClosedAtUtc,
    long Revision,
    IReadOnlyList<PurchaseOrderLineDto> Lines,
    bool CanEdit,
    bool CanConfirm,
    bool CanCancel,
    bool CanClose,
    bool CanReopen);

public sealed record PurchaseOrderPageDto(
    IReadOnlyList<PurchaseOrderDto> PurchaseOrders,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record PurchaseOrderImportError(int Row, string Message);

public sealed record PurchaseOrderImportResult(
    int ImportedCount,
    IReadOnlyList<PurchaseOrderImportError> Errors);

/// <summary>
/// Validation evidence retained between receiving validation and the stock
/// movement write. The receiving use case owns the transaction/commit.
/// </summary>
public sealed record PurchaseOrderReceiptPlan(
    int PurchaseOrderId,
    int PurchaseOrderLineId,
    string PurchaseOrderNumber,
    int ItemId,
    int WarehouseId,
    decimal BaseQuantity,
    decimal? ExpectedBaseQuantity = null);

public interface IPurchaseOrderService
{
    Task<Result<PurchaseOrderPageDto>> ListAsync(
        PurchaseOrderListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> CreateAsync(
        PurchaseOrderInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> UpdateAsync(
        int id,
        PurchaseOrderInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> ConfirmAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> CloseAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderDto>> ReopenAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        PurchaseOrderListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<PurchaseOrderReceiptPlan>> ValidateReceiptAsync(
        int purchaseOrderId,
        int purchaseOrderLineId,
        int itemId,
        int warehouseId,
        decimal baseQuantity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds receipt history and updates the tracked order. It deliberately
    /// does not save or commit; the caller must commit it with the stock
    /// movement in one transaction.
    /// </summary>
    Task<Result> RecordReceiptAsync(
        PurchaseOrderReceiptPlan plan,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default);
}
