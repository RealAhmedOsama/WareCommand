using Wms.Application.Common;
using Wms.Application.Inbound;
using Wms.Application.Purchasing;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;

namespace Wms.Application.Receiving;

public enum ReceiptSortField
{
    DocumentNumber,
    Status,
    WarehouseCode,
    SupplierCode,
    ReceivedAtUtc,
    UpdatedAt
}

public sealed record ReceiptListQuery(
    int? WarehouseId = null,
    int? SupplierId = null,
    ReceiptStatus? Status = null,
    string? SearchTerm = null,
    DateTime? ReceivedFromUtc = null,
    DateTime? ReceivedToUtc = null,
    bool IncludeCancelled = true,
    ReceiptSortField SortBy = ReceiptSortField.ReceivedAtUtc,
    bool Descending = true,
    int Page = 1,
    int PageSize = 50);

public sealed record ReceiptLineInput(
    string ItemSku,
    decimal Quantity,
    string? UnitOfMeasure = null,
    string? PackagingCode = null,
    int? PurchaseOrderId = null,
    int? PurchaseOrderLineId = null,
    int? AdvanceShippingNoticeId = null,
    int? AdvanceShippingNoticeLineId = null,
    string? LotNumber = null,
    DateTime? ExpiryDate = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    decimal? AcceptedQuantity = null,
    decimal? RejectedQuantity = null,
    decimal? DamagedQuantity = null,
    decimal? QuarantinedQuantity = null,
    string? Notes = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record ReceiptInput(
    int WarehouseId,
    int ReceivingLocationId,
    int? SupplierId = null,
    int? PurchaseOrderId = null,
    int? AdvanceShippingNoticeId = null,
    int? DockLocationId = null,
    string SourceType = "MANUAL",
    string? SourceReference = null,
    string? ExternalReference = null,
    string? SessionReference = null,
    string? Notes = null,
    IReadOnlyList<ReceiptLineInput>? Lines = null);

public sealed record ReceiptLineLinkInput(
    ReceiptLineLinkType Type,
    string Reference,
    string? Notes = null);

public sealed record ReceiptCorrectionInput(
    string? Reason,
    string? Notes = null);

public sealed record ReceiptLineDto(
    int Id,
    int LineNumber,
    int ItemId,
    string ItemSku,
    string ItemName,
    string EnteredUnitOfMeasure,
    decimal EnteredQuantity,
    string BaseUnitOfMeasure,
    decimal ExpectedBaseQuantity,
    decimal ReceivedBaseQuantity,
    decimal AcceptedBaseQuantity,
    decimal RejectedBaseQuantity,
    decimal DamagedBaseQuantity,
    decimal QuarantinedBaseQuantity,
    decimal RemainingBaseQuantity,
    int? PurchaseOrderId,
    int? PurchaseOrderLineId,
    int? AdvanceShippingNoticeId,
    int? AdvanceShippingNoticeLineId,
    string? LotNumber,
    DateTime? ExpiryDate,
    string? SerialNumber,
    int? LicensePlateId,
    string? LicensePlateNumber,
    bool LicensePlateIsSscc,
    int InventoryStatusId,
    string? InventoryStatusCode,
    string? InventoryStatusName,
    string? Notes,
    IReadOnlyList<int> MovementIds,
    IReadOnlyList<ReceiptLineLinkDto> Links,
    bool IsFullyReceived,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public sealed record ReceiptLineLinkDto(
    int Id,
    ReceiptLineLinkType Type,
    string Reference,
    string UserId,
    string? Notes);

public sealed record ReceiptDto(
    int Id,
    string DocumentNumber,
    int WarehouseId,
    string WarehouseCode,
    int? SupplierId,
    string? SupplierCode,
    string? SupplierName,
    int? PurchaseOrderId,
    int? AdvanceShippingNoticeId,
    int? DockLocationId,
    int ReceivingLocationId,
    string SourceType,
    string? SourceReference,
    string? ExternalReference,
    string? SessionReference,
    string? Notes,
    ReceiptStatus Status,
    DateTime CreatedAt,
    DateTime? ReceivedAtUtc,
    DateTime? OpenedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    DateTime? ReversedAtUtc,
    DateTime? CorrectedAtUtc,
    int? CorrectedByReceiptId,
    long Revision,
    IReadOnlyList<ReceiptLineDto> Lines,
    bool CanEdit,
    bool CanComplete,
    bool CanCancel,
    bool CanReverse,
    bool CanCorrect);

public sealed record ReceiptPageDto(
    IReadOnlyList<ReceiptDto> Receipts,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ReceiptCorrectionDto(
    ReceiptDto Original,
    ReceiptDto Correction);

/// <summary>
/// Input used by the receiving transaction after PO/ASN validation and before
/// the stock movement is created.
/// </summary>
public sealed record ReceiptReceivingInput(
    int WarehouseId,
    int ReceivingLocationId,
    int ItemId,
    string ItemSku,
    string ItemName,
    Quantity Quantity,
    string? LotNumber,
    DateTime? ExpiryDate,
    string? SerialNumber,
    int? LicensePlateId,
    int? PurchaseOrderId,
    int? PurchaseOrderLineId,
    int? AdvanceShippingNoticeId,
    int? AdvanceShippingNoticeLineId,
    PurchaseOrderReceiptPlan? PurchaseOrderPlan,
    AdvanceShippingNoticeReceiptPlan? AdvanceShippingNoticePlan,
    string? ReferenceNumber,
    string? Notes,
    string SourceType = "RECEIVING",
    string? SourceReference = null,
    int? SupplierId = null,
    int? DockLocationId = null,
    decimal? AcceptedBaseQuantity = null,
    decimal? RejectedBaseQuantity = null,
    decimal? DamagedBaseQuantity = null,
    decimal? QuarantinedBaseQuantity = null,
    string? SessionReference = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record ReceiptReceivingPlan(
    int ReceiptId,
    int ReceiptLineId,
    string DocumentNumber,
    int WarehouseId,
    int ItemId,
    decimal BaseQuantity,
    decimal AcceptedBaseQuantity,
    decimal RejectedBaseQuantity,
    decimal DamagedBaseQuantity,
    decimal QuarantinedBaseQuantity,
    PurchaseOrderReceiptPlan? PurchaseOrderPlan,
    AdvanceShippingNoticeReceiptPlan? AdvanceShippingNoticePlan,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public interface IReceiptService
{
    Task<Result<ReceiptPageDto>> ListAsync(
        ReceiptListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptDto>> GetAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptDto>> CreateAsync(
        ReceiptInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptDto>> CompleteAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptDto>> CancelAsync(
        int id,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptDto>> ReverseAsync(
        int id,
        string userId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptCorrectionDto>> CorrectAsync(
        int id,
        ReceiptCorrectionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<string>> ExportAsync(
        ReceiptListQuery request,
        CancellationToken cancellationToken = default);

    Task<Result<ReceiptReceivingPlan>> OpenForReceivingAsync(
        ReceiptReceivingInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> FinalizeReceivingAsync(
        ReceiptReceivingPlan plan,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> AddLinkAsync(
        int receiptLineId,
        ReceiptLineLinkInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
