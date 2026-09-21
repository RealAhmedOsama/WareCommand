using Wms.Application.Common;
using Wms.Application.WarehouseWork;
using Wms.Domain.Enums;

namespace Wms.Application.Workforce;

public sealed record WorkerProfileInput(
    int WarehouseId,
    string? TeamCode = null,
    string? ShiftCode = null,
    DateTime? ShiftStartAtUtc = null,
    DateTime? ShiftEndAtUtc = null,
    string TimeZoneId = "UTC",
    IReadOnlyList<string>? SkillCodes = null,
    IReadOnlyList<string>? CertificationCodes = null,
    IReadOnlyList<int>? PreferredZoneLocationIds = null,
    bool IsActive = true);

public sealed record WorkerProfileDto(
    int Id,
    string UserId,
    int WarehouseId,
    string? TeamCode,
    string? ShiftCode,
    DateTime? ShiftStartAtUtc,
    DateTime? ShiftEndAtUtc,
    string TimeZoneId,
    IReadOnlyList<string> SkillCodes,
    IReadOnlyList<string> CertificationCodes,
    IReadOnlyList<int> PreferredZoneLocationIds,
    bool IsActive,
    long Revision);

public sealed record WorkQueueInput(
    int WarehouseId,
    string Code,
    string Name,
    WarehouseWorkType WorkType,
    int? ZoneLocationId = null,
    int Priority = 50,
    int? Capacity = null,
    string? RequiredTeamCode = null,
    WarehouseWorkAssignmentStrategy AssignmentStrategy = WarehouseWorkAssignmentStrategy.Manual,
    IReadOnlyList<string>? RequiredSkillCodes = null,
    IReadOnlyList<string>? RequiredCertificationCodes = null,
    bool IsActive = true);

public sealed record WorkQueueDto(
    int Id,
    int WarehouseId,
    string Code,
    string Name,
    WarehouseWorkType WorkType,
    int? ZoneLocationId,
    int Priority,
    int? Capacity,
    string? RequiredTeamCode,
    WarehouseWorkAssignmentStrategy AssignmentStrategy,
    IReadOnlyList<string> RequiredSkillCodes,
    IReadOnlyList<string> RequiredCertificationCodes,
    bool IsActive,
    long Revision);

public sealed record WorkforceSuggestionsQuery(
    int WarehouseId,
    WarehouseWorkType? WorkType = null,
    string? QueueCode = null,
    int Limit = 50);

public sealed record WorkSuggestionDto(
    WarehouseWorkDto Work,
    string RoutingReason,
    int? QueueId,
    string QueueCode,
    int? ZoneLocationId);

public sealed record WorkforceSuggestionsDto(
    IReadOnlyList<WorkSuggestionDto> Suggestions,
    DateTime AsOfUtc);

public sealed record WorkforceActivityInput(
    int WorkId,
    WarehouseWorkActivityCategory Category,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc = null,
    string? Reason = null);

public sealed record WorkforceActivityDto(
    int Id,
    int WorkId,
    int WarehouseId,
    string WorkerUserId,
    WarehouseWorkActivityCategory Category,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    string? Reason,
    long Revision);

public sealed record WorkforceMetricsQuery(
    int WarehouseId,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? QueueCode = null,
    string? WorkerUserId = null);

public sealed record WorkforceMetricBucketDto(
    string QueueCode,
    int? ZoneLocationId,
    int OpenCount,
    int AssignedCount,
    int CompletedCount,
    int ExceptionCount,
    int CompletedLines,
    decimal CompletedUnits,
    double? AverageActiveMinutes,
    double TravelMinutes,
    double WaitMinutes,
    double ExceptionMinutes);

public sealed record WorkforceMetricsDto(
    int WarehouseId,
    DateTime FromUtc,
    DateTime ToUtc,
    int OpenCount,
    int AssignedCount,
    int CompletedCount,
    int ExceptionCount,
    int CompletedLines,
    decimal CompletedUnits,
    double? AverageActiveMinutes,
    IReadOnlyList<WorkforceMetricBucketDto> Buckets,
    string InterpretationNote);

public interface IWarehouseWorkAssignmentEligibilityService
{
    Task<Result> ValidateAsync(
        int workId,
        string? targetUserId,
        string? teamCode,
        bool supervisorOverride,
        bool selfClaim,
        CancellationToken cancellationToken = default);
}

public interface IWorkforceService
{
    Task<Result<WorkerProfileDto>> SaveWorkerProfileAsync(
        string userId,
        WorkerProfileInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WorkerProfileDto>>> ListWorkerProfilesAsync(
        int warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<WorkQueueDto>> SaveQueueAsync(
        int? queueId,
        WorkQueueInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WorkQueueDto>>> ListQueuesAsync(
        int warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<WorkforceSuggestionsDto>> SuggestAsync(
        WorkforceSuggestionsQuery query,
        string workerUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WarehouseWorkDto>> ClaimAsync(
        int workId,
        WarehouseWorkClaimInput input,
        string workerUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WorkforceActivityDto>> RecordActivityAsync(
        WorkforceActivityInput input,
        string workerUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WorkforceMetricsDto>> GetMetricsAsync(
        WorkforceMetricsQuery query,
        CancellationToken cancellationToken = default);
}
