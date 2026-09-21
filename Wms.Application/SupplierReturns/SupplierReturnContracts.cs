using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.SupplierReturns;

public sealed record SupplierReturnLineInput(
    int ItemId,
    decimal RequestedBaseQuantity,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int InventoryStatusId,
    string Reason,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int? PurchaseOrderLineId = null,
    int? AdvanceShippingNoticeLineId = null,
    int? ReceiptLineId = null,
    int? QualityInspectionId = null,
    int? QualityInspectionDispositionId = null,
    string? SourceReference = null);

public sealed record SupplierReturnCreateInput(
    string ReturnNumber,
    int WarehouseId,
    int SupplierId,
    int StagingLocationId,
    IReadOnlyList<SupplierReturnLineInput> Lines,
    string SourceType,
    string Reason,
    int? PurchaseOrderId = null,
    int? AdvanceShippingNoticeId = null,
    int? ReceiptId = null,
    int? QualityInspectionId = null,
    string? SupplierAuthorizationReference = null,
    string? ExternalReference = null);

public sealed record SupplierReturnCommandInput(
    int SupplierReturnId,
    string IdempotencyKey,
    string? Reason = null,
    bool SupervisorOverride = false,
    string? OverrideReason = null);

public sealed record SupplierReturnShipInput(
    int SupplierReturnId,
    string CarrierCode,
    string? TrackingNumber,
    string? ShippingDocumentReference,
    string IdempotencyKey);

public sealed record SupplierReturnLineDto(
    int Id,
    int LineNumber,
    int ItemId,
    string ItemSku,
    string ItemName,
    decimal RequestedBaseQuantity,
    decimal ApprovedBaseQuantity,
    decimal ReservedBaseQuantity,
    decimal StagedBaseQuantity,
    decimal ShippedBaseQuantity,
    decimal RemainingToStage,
    decimal RemainingToShip,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int InventoryStatusId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int? PurchaseOrderLineId,
    int? AdvanceShippingNoticeLineId,
    int? ReceiptLineId,
    int? QualityInspectionId,
    int? QualityInspectionDispositionId,
    string Reason,
    string? SourceReference,
    long Revision);

public sealed record SupplierReturnDto(
    int Id,
    string ReturnNumber,
    int WarehouseId,
    int SupplierId,
    int StagingLocationId,
    string SourceType,
    string Reason,
    string? SupplierAuthorizationReference,
    string? ExternalReference,
    int? PurchaseOrderId,
    int? AdvanceShippingNoticeId,
    int? ReceiptId,
    int? QualityInspectionId,
    int? WarehouseWorkId,
    SupplierReturnStatus Status,
    decimal RequestedQuantity,
    decimal StagedQuantity,
    decimal ShippedQuantity,
    string? CarrierCode,
    string? TrackingNumber,
    string? ShippingDocumentReference,
    DateTime CreatedAtUtc,
    DateTime? ApprovedAtUtc,
    DateTime? ReleasedAtUtc,
    DateTime? PackedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? AcknowledgedAtUtc,
    DateTime? ClosedAtUtc,
    DateTime? CancelledAtUtc,
    string? ExceptionReason,
    long Revision,
    IReadOnlyList<SupplierReturnLineDto> Lines);

public sealed record SupplierReturnQuery(
    int? WarehouseId = null,
    int? SupplierId = null,
    SupplierReturnStatus? Status = null,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 50);

public sealed record SupplierReturnPageDto(
    IReadOnlyList<SupplierReturnDto> Returns,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public interface ISupplierReturnService
{
    Task<Result<SupplierReturnDto>> CreateAsync(
        SupplierReturnCreateInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnPageDto>> ListAsync(
        SupplierReturnQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> GetAsync(
        int supplierReturnId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> ApproveAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> ReleaseAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> PackAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> ShipAsync(
        SupplierReturnShipInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> AcknowledgeAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> CloseAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> ExceptionAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SupplierReturnDto>> CancelAsync(
        SupplierReturnCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
