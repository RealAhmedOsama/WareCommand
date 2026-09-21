using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.Quality;

public sealed record QualityProfileTestInput(
    int Sequence,
    string Code,
    string Name,
    string LocalizedName,
    QualityMeasurementType MeasurementType,
    bool IsRequired = true,
    decimal? MinimumValue = null,
    decimal? MaximumValue = null,
    string? AllowedValues = null);

public sealed record QualityProfileInput(
    string Code,
    string Name,
    string LocalizedName,
    QualityRiskLevel RiskLevel,
    QualitySamplingMethod SamplingMethod,
    decimal SamplingValue,
    bool IsRequired = true,
    int? WarehouseId = null,
    int? SupplierId = null,
    int? ItemId = null,
    string? ItemCategory = null,
    string? ReceiptSourceType = null,
    IReadOnlyList<QualityProfileTestInput>? Tests = null);

public sealed record QualityProfileDto(
    int Id,
    string Code,
    string Name,
    string LocalizedName,
    QualityRiskLevel RiskLevel,
    QualitySamplingMethod SamplingMethod,
    decimal SamplingValue,
    bool IsRequired,
    int? WarehouseId,
    int? SupplierId,
    int? ItemId,
    string? ItemCategory,
    string? ReceiptSourceType,
    bool IsActive,
    long Revision,
    IReadOnlyList<QualityProfileTestDto> Tests);

public sealed record QualityProfileTestDto(
    int Id,
    int Sequence,
    string Code,
    string Name,
    string LocalizedName,
    QualityMeasurementType MeasurementType,
    bool IsRequired,
    decimal? MinimumValue,
    decimal? MaximumValue,
    string? AllowedValues);

public sealed record QualityInspectionTestResultInput(
    int QualityProfileTestId,
    decimal InspectedQuantity,
    string? RecordedValue,
    decimal? NumericValue,
    bool? BooleanValue,
    bool Passed,
    string? Notes = null,
    string? AttachmentReferences = null);

public sealed record QualityDispositionInput(
    QualityDispositionType Type,
    decimal Quantity,
    string Reason,
    string? ReferenceNumber = null,
    int? TargetLocationId = null,
    bool SupervisorOverride = false,
    string? OverrideReason = null);

public sealed record QualityInspectionQuery(
    int? WarehouseId = null,
    QualityInspectionStatus? Status = null,
    int? ReceiptId = null,
    int? ItemId = null,
    int? SupplierId = null,
    bool IncludeClosed = true,
    int Page = 1,
    int PageSize = 50);

public sealed record QualityInspectionPageDto(
    IReadOnlyList<QualityInspectionDto> Inspections,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record QualityInspectionTestResultDto(
    int Id,
    int QualityProfileTestId,
    string TestCode,
    string TestName,
    QualityMeasurementType MeasurementType,
    decimal? NumericValue,
    bool? BooleanValue,
    string? RecordedValue,
    bool Passed,
    decimal? InspectedQuantity,
    string? Notes,
    string? AttachmentReferences,
    string RecordedByUserId,
    DateTime RecordedAtUtc);

public sealed record QualityInspectionDispositionDto(
    int Id,
    QualityDispositionType Type,
    decimal Quantity,
    int TargetInventoryStatusId,
    string Reason,
    string RecordedByUserId,
    DateTime RecordedAtUtc,
    string? ReferenceNumber,
    int? MovementId,
    int? TargetLocationId,
    bool SupervisorOverride,
    string? OverrideReason);

public sealed record QualityInspectionDto(
    int Id,
    string InspectionNumber,
    int ReceiptId,
    int ReceiptLineId,
    int WarehouseId,
    int ItemId,
    string ItemSku,
    string ItemName,
    decimal ReceivedBaseQuantity,
    decimal SampleBaseQuantity,
    decimal InspectedBaseQuantity,
    decimal AcceptedBaseQuantity,
    decimal RejectedBaseQuantity,
    decimal RemainingSampleQuantity,
    int InventoryStatusId,
    string? SourceType,
    int? SupplierId,
    string? SupplierCode,
    int? QualityProfileId,
    string? QualityProfileCode,
    QualityRiskLevel RiskLevel,
    string? LotNumber,
    DateTime? ExpiryDate,
    string? SerialNumber,
    int? LicensePlateId,
    string? LicensePlateNumber,
    QualityInspectionStatus Status,
    string CreatedByUserId,
    string? InspectorUserId,
    DateTime CreatedAt,
    DateTime? StartedAtUtc,
    DateTime? ClosedAtUtc,
    string? ClosedByUserId,
    string? ClosureReason,
    long Revision,
    IReadOnlyList<QualityInspectionTestResultDto> Results,
    IReadOnlyList<QualityInspectionDispositionDto> Dispositions,
    bool IsClosed);

public interface IQualityInspectionService
{
    Task<bool> RequiresInboundInspectionAsync(
        int warehouseId,
        int itemId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityProfilePageDto>> ListProfilesAsync(
        bool includeInactive = false,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<Result<QualityProfileDto>> CreateProfileAsync(
        QualityProfileInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityProfileDto>> SetProfileActiveAsync(
        int profileId,
        bool isActive,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityInspectionPageDto>> ListInspectionsAsync(
        QualityInspectionQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<QualityInspectionDto>> GetInspectionAsync(
        int inspectionId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityInspectionDto?>> EnsureForReceiptAsync(
        int receiptId,
        int receiptLineId,
        Movement movement,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityInspectionDto>> RecordTestResultAsync(
        int inspectionId,
        QualityInspectionTestResultInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityInspectionDto>> AddDispositionAsync(
        int inspectionId,
        QualityDispositionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<QualityInspectionDto>> CloseAsync(
        int inspectionId,
        string userId,
        string? reason = null,
        bool supervisorOverride = false,
        CancellationToken cancellationToken = default);
}

public sealed record QualityProfilePageDto(
    IReadOnlyList<QualityProfileDto> Profiles,
    int TotalCount);
