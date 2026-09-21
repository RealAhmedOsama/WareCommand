using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Outbound;

public sealed record OutboundExceptionQuery(
    int? WarehouseId = null,
    OutboundExceptionCode? Code = null,
    OutboundExceptionSeverity? Severity = null,
    OutboundExceptionStatus? Status = null,
    string? QueueCode = null,
    string? OwnerUserId = null,
    int? SalesOrderId = null,
    int? ShipmentId = null,
    string? SearchTerm = null,
    bool IncludeClosed = false,
    bool OverdueOnly = false,
    DateTime? AsOfUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record OutboundExceptionInput(
    int WarehouseId,
    OutboundExceptionCode Code,
    OutboundExceptionSeverity Severity,
    string Reason,
    string IdempotencyKey,
    string? QueueCode = null,
    DateTime? DueAtUtc = null,
    int? SalesOrderId = null,
    int? SalesOrderLineId = null,
    int? InventoryReservationId = null,
    int? WarehouseWorkId = null,
    int? WarehouseWorkLineId = null,
    int? ShipmentId = null,
    int? ShipmentPackageId = null,
    int? ShipmentLoadId = null,
    int? ItemId = null,
    int? LocationId = null,
    int? LotId = null,
    int? SerialNumberId = null,
    int? LicensePlateId = null,
    decimal? ExpectedBaseQuantity = null,
    decimal? ActualBaseQuantity = null,
    decimal? VarianceBaseQuantity = null,
    string? ItemSkuSnapshot = null,
    string? Notes = null,
    string? AttachmentReferences = null);

public sealed record OutboundExceptionAssignmentInput(
    string? OwnerUserId,
    string? TeamCode);

public sealed record OutboundExceptionResolutionInput(
    OutboundExceptionResolution Resolution,
    string Reason,
    decimal? Quantity = null,
    int? SubstituteItemId = null,
    int? NewCarrierId = null,
    int? NewCarrierServiceId = null,
    bool SupervisorOverride = false,
    string? OverrideReason = null);

public sealed record OutboundExceptionDto(
    int Id,
    int WarehouseId,
    string ExceptionNumber,
    string IdempotencyKey,
    OutboundExceptionCode Code,
    OutboundExceptionSeverity Severity,
    OutboundExceptionStatus Status,
    string Reason,
    string QueueCode,
    DateTime? DueAtUtc,
    int? SalesOrderId,
    int? SalesOrderLineId,
    int? InventoryReservationId,
    int? WarehouseWorkId,
    int? WarehouseWorkLineId,
    int? ShipmentId,
    int? ShipmentPackageId,
    int? ShipmentLoadId,
    int? ItemId,
    int? LocationId,
    int? LotId,
    int? SerialNumberId,
    int? LicensePlateId,
    decimal? ExpectedBaseQuantity,
    decimal? ActualBaseQuantity,
    decimal? VarianceBaseQuantity,
    string? ItemSkuSnapshot,
    string? Notes,
    string? AttachmentReferences,
    string CreatedByUserId,
    string? OwnerUserId,
    string? AssignedTeamCode,
    string? AssignedByUserId,
    DateTime? AssignedAtUtc,
    string? ReviewStartedByUserId,
    DateTime? ReviewStartedAtUtc,
    OutboundExceptionResolution? Resolution,
    string? ResolutionReason,
    string? ResolutionDetails,
    string? ResolvedByUserId,
    DateTime? ResolvedAtUtc,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    long Revision,
    bool IsTerminal,
    bool IsOverdue);

public sealed record OutboundExceptionPageDto(
    IReadOnlyList<OutboundExceptionDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int OpenCount,
    int OverdueCount);

public interface IOutboundExceptionService
{
    Task<Result<OutboundExceptionPageDto>> ListAsync(
        OutboundExceptionQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<OutboundExceptionDto>> GetAsync(
        int exceptionId,
        CancellationToken cancellationToken = default);

    Task<Result<OutboundExceptionDto>> CreateAsync(
        OutboundExceptionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<OutboundExceptionDto>> AssignAsync(
        int exceptionId,
        OutboundExceptionAssignmentInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<OutboundExceptionDto>> StartReviewAsync(
        int exceptionId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<OutboundExceptionDto>> ResolveAsync(
        int exceptionId,
        string idempotencyKey,
        OutboundExceptionResolutionInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
