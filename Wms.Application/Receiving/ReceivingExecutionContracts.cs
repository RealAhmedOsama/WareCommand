using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record ReceivingSessionStartInput(
    int WarehouseId,
    int ReceivingLocationId,
    ReceivingSessionSourceType SourceType,
    int? PurchaseOrderId = null,
    int? AdvanceShippingNoticeId = null,
    int? DockLocationId = null,
    string? ExternalReference = null,
    string? SessionReference = null,
    string? Notes = null,
    bool SupervisorOverride = false,
    string? SupervisorOverrideReason = null);

public sealed record ReceivingScanInput(
    string ClientOperationId,
    string RawScanValue,
    string? ItemSku = null,
    decimal? Quantity = null,
    string? UnitOfMeasure = null,
    string? PackagingCode = null,
    string? LotNumber = null,
    DateTime? ExpiryDate = null,
    DateTime? ManufacturedDate = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    string? LicensePlateNumber = null,
    bool CreateLicensePlate = false,
    string? DestinationLocationCode = null,
    string? ReferenceNumber = null,
    string? Notes = null,
    string? SupervisorOverrideReason = null,
    int? PurchaseOrderLineId = null,
    int? AdvanceShippingNoticeLineId = null);

public sealed record ReceivingSessionCompletionInput(
    bool AllowPartial = false,
    string? SupervisorOverrideReason = null);

public sealed record ReceivingSessionCorrectionInput(
    string Reason,
    string? Notes = null);

public sealed record ReceivingSessionLineDto(
    int Id,
    int LineNumber,
    int ItemId,
    string ItemSku,
    string ItemName,
    string BaseUnitOfMeasure,
    decimal? ExpectedBaseQuantity,
    decimal PreviouslyReceivedBaseQuantity,
    decimal ReceivedBaseQuantity,
    decimal? RemainingBaseQuantity,
    decimal? MaximumReceivableBaseQuantity,
    decimal OverDeliveryTolerancePercent,
    decimal UnderDeliveryTolerancePercent,
    int? PurchaseOrderLineId,
    int? AdvanceShippingNoticeLineId,
    string? ExpectedLotNumber,
    DateTime? ExpectedExpiryDate,
    string? ExpectedSerialNumber,
    string? ExpectedLicensePlateNumber,
    bool IsFullyReceived,
    long Revision);

public sealed record ReceivingScanDto(
    int Id,
    int? SessionLineId,
    string ClientOperationId,
    string RawScanValue,
    string ItemSku,
    decimal EnteredQuantity,
    decimal? BaseQuantity,
    string? UnitOfMeasure,
    string? PackagingCode,
    string? LotNumber,
    DateTime? ExpiryDate,
    string? SerialNumber,
    int? LicensePlateId,
    string? DestinationLocationCode,
    ReceivingScanStatus Status,
    int? MovementId,
    int? ReceiptId,
    string? ReceiptDocumentNumber,
    string? IdempotencyKey,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CorrectedAtUtc,
    string? CorrectionReason,
    long Revision);

public sealed record ReceivingSessionDto(
    int Id,
    int WarehouseId,
    int ReceivingLocationId,
    ReceivingSessionSourceType SourceType,
    string SessionReference,
    int? PurchaseOrderId,
    int? AdvanceShippingNoticeId,
    int? DockLocationId,
    string? ExternalReference,
    string? Notes,
    ReceivingSessionStatus Status,
    string CreatedByUserId,
    bool SupervisorOverride,
    string? SupervisorOverrideReason,
    DateTime OpenedAtUtc,
    DateTime? PausedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledByUserId,
    string? CancellationReason,
    DateTime LastActivityAtUtc,
    long Revision,
    IReadOnlyList<ReceivingSessionLineDto> Lines,
    IReadOnlyList<ReceivingScanDto> Scans,
    bool CanScan,
    bool HasOpenDemand);

public interface IReceivingExecutionService
{
    Task<Result<ReceivingSessionDto>> StartAsync(
        ReceivingSessionStartInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> GetAsync(
        int sessionId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> PauseAsync(
        int sessionId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> ResumeAsync(
        int sessionId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> ScanAsync(
        int sessionId,
        ReceivingScanInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> CompleteAsync(
        int sessionId,
        ReceivingSessionCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> CancelAsync(
        int sessionId,
        string reason,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceivingSessionDto>> CorrectScanAsync(
        int sessionId,
        int scanId,
        ReceivingSessionCorrectionInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
