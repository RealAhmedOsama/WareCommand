using Wms.Application.Common;
using Wms.Application.Purchasing;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Inbound;

public enum AdvanceShippingNoticeSortField
{
    DocumentNumber,
    SupplierCode,
    Status,
    ExpectedArrivalFromUtc,
    UpdatedAt
}

public sealed record AdvanceShippingNoticeListQuery(
    int? WarehouseId = null,
    int? SupplierId = null,
    AdvanceShippingNoticeStatus? Status = null,
    string? SearchTerm = null,
    DateTime? ExpectedArrivalFromUtc = null,
    DateTime? ExpectedArrivalToUtc = null,
    bool IncludeCancelled = true,
    AdvanceShippingNoticeSortField SortBy = AdvanceShippingNoticeSortField.ExpectedArrivalFromUtc,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

public sealed record AdvanceShippingNoticeLineInput(
    string ItemSku,
    decimal ExpectedQuantity,
    string? UnitOfMeasure = null,
    string? PackagingCode = null,
    int? PurchaseOrderId = null,
    int? PurchaseOrderLineId = null,
    decimal? OverDeliveryTolerancePercent = null,
    decimal? UnderDeliveryTolerancePercent = null,
    string? PreAdvisedLotNumber = null,
    DateTime? PreAdvisedExpiryDate = null,
    string? PreAdvisedSerialNumber = null,
    string? ExpectedLicensePlateNumber = null,
    bool ExpectedLicensePlateIsSscc = false,
    string? Notes = null);

public sealed record AdvanceShippingNoticeInput(
    int WarehouseId,
    int SupplierId,
    string? CarrierName = null,
    DateTime? ExpectedArrivalFromUtc = null,
    DateTime? ExpectedArrivalToUtc = null,
    string? VehicleNumber = null,
    string? TrailerNumber = null,
    string? ContainerNumber = null,
    string? TrackingReference = null,
    string? ExternalReference = null,
    string SourceType = "MANUAL",
    string? SourceReference = null,
    string? SourcePayload = null,
    int? DockLocationId = null,
    string? Notes = null,
    bool AllowMultiplePurchaseOrders = true,
    IReadOnlyList<AdvanceShippingNoticeLineInput>? Lines = null);

public sealed record AdvanceShippingNoticeLineDto(
    int Id,
    int LineNumber,
    int ItemId,
    string ItemSku,
    string ItemName,
    string EnteredUnitOfMeasure,
    decimal ExpectedQuantity,
    string BaseUnitOfMeasure,
    decimal ExpectedBaseQuantity,
    decimal ReceivedQuantity,
    decimal ReceivedBaseQuantity,
    decimal RemainingBaseQuantity,
    decimal OverDeliveryTolerancePercent,
    decimal UnderDeliveryTolerancePercent,
    int? ItemPackagingId,
    string? ItemPackagingCode,
    int? PurchaseOrderId,
    int? PurchaseOrderLineId,
    string? PreAdvisedLotNumber,
    DateTime? PreAdvisedExpiryDate,
    string? PreAdvisedSerialNumber,
    string? ExpectedLicensePlateNumber,
    bool ExpectedLicensePlateIsSscc,
    string? Notes,
    bool IsClosed,
    bool IsFullyReceived,
    string ConversionPath,
    string ConversionRuleIds,
    decimal ConversionFactorToBase,
    int ConversionPrecision);

public sealed record AdvanceShippingNoticeDiscrepancyDto(
    int Id,
    int? AdvanceShippingNoticeLineId,
    AdvanceShippingNoticeDiscrepancyKind Kind,
    decimal? ExpectedBaseQuantity,
    decimal? ReceivedBaseQuantity,
    decimal? VarianceBaseQuantity,
    string Details,
    string RecordedByUserId,
    DateTime OccurredAtUtc);

public sealed record AdvanceShippingNoticeDto(
    int Id,
    string DocumentNumber,
    int WarehouseId,
    string WarehouseCode,
    int SupplierId,
    string SupplierCode,
    string SupplierName,
    string? CarrierName,
    DateTime? ExpectedArrivalFromUtc,
    DateTime? ExpectedArrivalToUtc,
    string? VehicleNumber,
    string? TrailerNumber,
    string? ContainerNumber,
    string? TrackingReference,
    string? ExternalReference,
    string SourceType,
    string? SourceReference,
    string? SourcePayload,
    int? DockLocationId,
    string? Notes,
    AdvanceShippingNoticeStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? SubmittedAtUtc,
    DateTime? ArrivedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    long Revision,
    IReadOnlyList<AdvanceShippingNoticeLineDto> Lines,
    IReadOnlyList<AdvanceShippingNoticeDiscrepancyDto> Discrepancies,
    bool CanEdit,
    bool CanSubmit,
    bool CanArrive,
    bool CanReceive,
    bool CanCancel);

public sealed record AdvanceShippingNoticePageDto(
    IReadOnlyList<AdvanceShippingNoticeDto> AdvanceShippingNotices,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdvanceShippingNoticeImportError(int Row, string Message);

public sealed record AdvanceShippingNoticeImportResult(
    int ImportedCount,
    IReadOnlyList<AdvanceShippingNoticeImportError> Errors);

public sealed record AdvanceShippingNoticeReceiptPlan(
    int AdvanceShippingNoticeId,
    int AdvanceShippingNoticeLineId,
    string DocumentNumber,
    int ItemId,
    int WarehouseId,
    decimal BaseQuantity,
    string? PreAdvisedLotNumber,
    DateTime? PreAdvisedExpiryDate,
    string? PreAdvisedSerialNumber,
    string? ExpectedLicensePlateNumber,
    PurchaseOrderReceiptPlan? PurchaseOrderPlan);

public sealed record AdvanceShippingNoticeDiscrepancyInput(
    int? AdvanceShippingNoticeLineId,
    AdvanceShippingNoticeDiscrepancyKind Kind,
    decimal? ExpectedBaseQuantity,
    decimal? ReceivedBaseQuantity,
    decimal? VarianceBaseQuantity,
    string Details);

public interface IAdvanceShippingNoticeService
{
    Task<Result<AdvanceShippingNoticePageDto>> ListAsync(
        AdvanceShippingNoticeListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> CreateAsync(
        AdvanceShippingNoticeInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> UpdateAsync(
        int id,
        AdvanceShippingNoticeInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> SubmitAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> MarkExpectedAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> AssignDockAsync(
        int id,
        int? dockLocationId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> ArriveAsync(
        int id,
        int? dockLocationId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> MarkExceptionAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> CompleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeImportResult>> ImportAsync(
        string csv,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        AdvanceShippingNoticeListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeReceiptPlan>> ValidateReceiptAsync(
        int advanceShippingNoticeId,
        int advanceShippingNoticeLineId,
        int itemId,
        int warehouseId,
        decimal baseQuantity,
        string? lotNumber,
        DateTime? expiryDate,
        string? serialNumber,
        int? licensePlateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds ASN and optional PO receipt history without saving. The receiving
    /// use case owns the movement/ASN/PO transaction and commit.
    /// </summary>
    Task<Result> RecordReceiptAsync(
        AdvanceShippingNoticeReceiptPlan plan,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<AdvanceShippingNoticeDto>> AddDiscrepancyAsync(
        int id,
        AdvanceShippingNoticeDiscrepancyInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
