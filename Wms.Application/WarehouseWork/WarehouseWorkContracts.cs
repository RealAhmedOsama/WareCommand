using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Application.WarehouseWork;

public sealed record WarehouseWorkLineInput(
    int Sequence,
    int WarehouseId,
    int ItemId,
    decimal PlannedQuantity,
    string BaseUnitOfMeasure,
    int? SourceLocationId = null,
    int? DestinationLocationId = null,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? LicensePlateId = null,
    int? InventoryStatusId = null,
    string? SourceReference = null,
    string? DimensionsSnapshot = null,
    int? ReservationId = null,
    int? ReservationAllocationId = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record WarehouseWorkInput(
    string CreationKey,
    WarehouseWorkType Type,
    int WarehouseId,
    string SourceEntityType,
    string SourceEntityId,
    int Priority = 50,
    string? SourceLineReference = null,
    string? QueueCode = null,
    DateTime? DueAtUtc = null,
    string? TeamCode = null,
    string? Notes = null,
    bool MakeAvailable = true,
    IReadOnlyList<WarehouseWorkLineInput>? Lines = null,
    int? AllocationStrategyPolicyId = null,
    string? AllocationStrategyKey = null,
    InventoryAllocationStrategyKind? AllocationStrategy = null,
    long? AllocationStrategyRevision = null);

public sealed record WarehouseWorkAssignmentInput(
    string? UserId,
    string? TeamCode,
    string IdempotencyKey,
    bool SupervisorOverride = false,
    string? OverrideReason = null);

public sealed record WarehouseWorkClaimInput(
    string IdempotencyKey,
    string? TeamCode = null);

public sealed record WarehouseWorkCommandInput(
    string IdempotencyKey,
    string? Reason = null);

public sealed record WarehouseWorkExceptionInput(
    WarehouseWorkExceptionType ExceptionType,
    string Reason,
    string IdempotencyKey);

public sealed record WarehouseWorkLineActualInput(
    int LineId,
    decimal ActualQuantity);

public sealed record WarehouseWorkScanInput(
    int LineId,
    int ItemId,
    int SourceLocationId,
    int DestinationLocationId,
    decimal ActualQuantity,
    int? LicensePlateId = null,
    bool DestinationOverride = false,
    int? LotId = null,
    int? SerialNumberId = null,
    string? SerialNumber = null,
    int? TargetLicensePlateId = null);

public sealed record WarehouseWorkCompletionInput(
    string IdempotencyKey,
    IReadOnlyList<WarehouseWorkLineActualInput>? Lines = null,
    bool SupervisorOverride = false,
    string? OverrideReason = null,
    string? CompletionReference = null,
    IReadOnlyList<WarehouseWorkScanInput>? Scans = null);

public sealed record PutawayWorkGenerationInput(
    int ReceiptId,
    int ReceiptLineId,
    int WarehouseId,
    int ItemId,
    decimal Quantity,
    string BaseUnitOfMeasure,
    int SourceLocationId,
    int? LicensePlateId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int InventoryStatusId,
    string? SourceReference,
    string MovementKey,
    bool QualityInspectionPending,
    int Priority = 50,
    string? Notes = null,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string? OwnerCodeSnapshot = null);

public sealed record WarehouseWorkQuery(
    int? WarehouseId = null,
    WarehouseWorkType? Type = null,
    WarehouseWorkStatus? Status = null,
    string? AssignedUserId = null,
    string? QueueCode = null,
    bool IncludeTerminal = true,
    int Page = 1,
    int PageSize = 50);

public sealed record WarehouseWorkLineDto(
    int Id,
    int Sequence,
    int WarehouseId,
    int ItemId,
    decimal PlannedQuantity,
    decimal ActualQuantity,
    string BaseUnitOfMeasure,
    int? SourceLocationId,
    int? DestinationLocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int? InventoryStatusId,
    string? SourceReference,
    string? DimensionsSnapshot,
    long Revision,
    int? ReservationId,
    int? ReservationAllocationId,
    InventoryOwnerKind OwnerKind = InventoryOwnerKind.CompanyOwned,
    int? InventoryOwnerId = null,
    string OwnerCodeSnapshot = InventoryOwnershipDimension.CompanyOwnerCode);

public sealed record WarehouseWorkDto(
    int Id,
    string WorkNumber,
    string CreationKey,
    WarehouseWorkType Type,
    int WarehouseId,
    string SourceEntityType,
    string SourceEntityId,
    string? SourceLineReference,
    string QueueCode,
    int Priority,
    DateTime? DueAtUtc,
    string? TeamCode,
    string? Notes,
    WarehouseWorkStatus Status,
    string? AssignedUserId,
    string? AssignedTeamCode,
    string? AssignedByUserId,
    DateTime? AssignedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? PausedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CancelledAtUtc,
    string? CompletedByUserId,
    string? CancelledByUserId,
    string? CancellationReason,
    WarehouseWorkExceptionType? ExceptionType,
    string? ExceptionReason,
    DateTime? ExceptionAtUtc,
    bool SupervisorOverride,
    string? OverrideReason,
    long Revision,
    IReadOnlyList<WarehouseWorkLineDto> Lines,
    bool IsTerminal,
    bool HasExceptions,
    int? AllocationStrategyPolicyId = null,
    string? AllocationStrategyKey = null,
    InventoryAllocationStrategyKind? AllocationStrategy = null,
    long? AllocationStrategyRevision = null);

public sealed record WarehouseWorkPageDto(
    IReadOnlyList<WarehouseWorkDto> Work,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record WarehouseWorkHandlerResult(
    IReadOnlyList<WarehouseWorkLineActualInput> ActualLines,
    string? Summary = null,
    IReadOnlyList<int>? MovementIds = null);

public interface IWarehouseWorkCompletionHandler
{
    WarehouseWorkType WorkType { get; }

    Task<Result<WarehouseWorkHandlerResult>> ExecuteAsync(
        WarehouseWorkEntity work,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default);
}

public interface IWarehouseWorkService
{
    Task<Result<WarehouseWorkDto>> CreateAsync(
        WarehouseWorkInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> CreateForAllocationAsync(
        WarehouseWorkInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WarehouseWorkDto>>> EnsurePutawayForReceiptAsync(
        PutawayWorkGenerationInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkPageDto>> ListAsync(
        WarehouseWorkQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> GetAsync(
        int workId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> AssignAsync(
        int workId,
        WarehouseWorkAssignmentInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> ClaimAsync(
        int workId,
        WarehouseWorkClaimInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> ReleaseAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> StartAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> PauseAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> ResumeAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> RecordExceptionAsync(
        int workId,
        WarehouseWorkExceptionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> CancelAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> CancelForAllocationAsync(
        int workId,
        WarehouseWorkCommandInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> CompleteAsync(
        int workId,
        WarehouseWorkCompletionInput input,
        string userId,
        CancellationToken cancellationToken = default);
}
