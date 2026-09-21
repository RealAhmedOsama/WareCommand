using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Outbound;

public sealed record WaveTemplateInput(
    int WarehouseId,
    string TemplateKey,
    string Name,
    WaveTriggerType TriggerType = WaveTriggerType.Scheduled,
    int Priority = 50,
    int Limit = 100,
    bool ReleaseToWarehouse = true,
    string? ScheduleCron = null,
    DateOnly? RequestedShipDateFrom = null,
    DateOnly? RequestedShipDateTo = null,
    string? CarrierCode = null,
    int? CustomerId = null,
    int? MinimumPriority = null,
    string? SourceType = null);

public sealed record WaveTemplateQuery(
    int? WarehouseId = null,
    bool IncludeInactive = false);

public sealed record WaveTemplateDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    string TemplateKey,
    string Name,
    WaveTriggerType TriggerType,
    int Priority,
    int Limit,
    bool ReleaseToWarehouse,
    string? ScheduleCron,
    DateOnly? RequestedShipDateFrom,
    DateOnly? RequestedShipDateTo,
    string? CarrierCode,
    int? CustomerId,
    int? MinimumPriority,
    string? SourceType,
    bool IsActive,
    long Revision);

public sealed record WaveCreateInput(
    int WarehouseId,
    string CreationKey,
    WaveTriggerType TriggerType = WaveTriggerType.Manual,
    int Priority = 50,
    DateTime? PlannedStartAtUtc = null,
    DateTime? PlannedReleaseAtUtc = null,
    DateOnly? RequestedShipDateFrom = null,
    DateOnly? RequestedShipDateTo = null,
    string? CarrierCode = null,
    int? CustomerId = null,
    int? MinimumPriority = null,
    string? SourceType = null,
    int Limit = 100,
    bool ReleaseToWarehouse = true,
    int? TemplateId = null,
    string? TemplateKey = null);

public sealed record WaveProcessInput(
    string IdempotencyKey,
    bool ReleaseToWarehouse = true,
    int BatchSize = 100,
    string? Reason = null);

public sealed record WaveLineRemovalInput(
    string IdempotencyKey,
    string? Reason = null);

public sealed record WaveCancellationInput(
    string IdempotencyKey,
    string Reason);

public sealed record WaveQuery(
    int? WarehouseId = null,
    WaveStatus? Status = null,
    int? TemplateId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record WaveLineDto(
    int Id,
    int SalesOrderId,
    int SalesOrderLineId,
    int LineNumber,
    int ItemId,
    string ItemSku,
    decimal RequestedQuantity,
    decimal AllocatedQuantity,
    decimal BackorderQuantity,
    int? ReservationId,
    int WorkCount,
    WaveLineStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    string? Explanation,
    DateTime? ProcessedAtUtc,
    long Revision);

public sealed record WaveStepDto(
    int Id,
    WaveStepType Step,
    int Attempt,
    string IdempotencyKey,
    WaveStepStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int ItemsExamined,
    int ItemsSucceeded,
    int ItemsFailed,
    string? ErrorMessage,
    string? DetailsJson);

public sealed record WaveDto(
    int Id,
    int WarehouseId,
    string WaveNumber,
    string CreationKey,
    int? TemplateId,
    string? TemplateKey,
    WaveTriggerType TriggerType,
    int Priority,
    DateTime? PlannedStartAtUtc,
    DateTime? PlannedReleaseAtUtc,
    int CapacityLimit,
    WaveStatus Status,
    string? LastProcessIdempotencyKey,
    DateTime? LastProcessedAtUtc,
    string? LastError,
    long Revision,
    bool IsSimulation,
    IReadOnlyList<WaveLineDto> Lines,
    IReadOnlyList<WaveStepDto> History,
    int SelectedLineCount,
    int ProcessedLineCount,
    int FailedLineCount,
    int ShortageLineCount);

public sealed record WavePageDto(
    IReadOnlyList<WaveDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record WaveScheduledRunDto(
    int TemplatesExamined,
    int WavesCreated,
    int LinesSelected,
    IReadOnlyList<int> WaveIds);

public interface IWaveService
{
    Task<Result<WaveTemplateDto>> SaveTemplateAsync(
        int? templateId,
        WaveTemplateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<WaveTemplateDto>>> SearchTemplatesAsync(
        WaveTemplateQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<WaveDto>> CreateAsync(
        WaveCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WaveDto>> SimulateAsync(
        WaveCreateInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WavePageDto>> SearchAsync(
        WaveQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<WaveDto>> GetAsync(
        int waveId,
        CancellationToken cancellationToken = default);

    Task<Result<WaveDto>> ProcessAsync(
        int waveId,
        WaveProcessInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WaveDto>> RemoveLineAsync(
        int waveId,
        int salesOrderLineId,
        WaveLineRemovalInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WaveDto>> CancelAsync(
        int waveId,
        WaveCancellationInput input,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<WaveScheduledRunDto>> RunScheduledAsync(
        int? warehouseId,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
