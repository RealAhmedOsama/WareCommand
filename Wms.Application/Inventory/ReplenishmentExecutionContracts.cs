using Wms.Application.Common;

namespace Wms.Application.Inventory;

public sealed record ReplenishmentGenerationQuery(
    int? WarehouseId = null,
    int? PolicyId = null,
    int Limit = 200);

public sealed record ReplenishmentWorkPlanLineDto(
    int Sequence,
    int SourceLocationId,
    int DestinationLocationId,
    decimal PlannedQuantity,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    string BaseUnitOfMeasure,
    string SelectionReason);

public sealed record ReplenishmentWorkPlanDto(
    int PolicyId,
    int ItemId,
    string ItemSku,
    int WarehouseId,
    int? DestinationLocationId,
    decimal SignalShortfallQuantity,
    decimal OpenWorkQuantity,
    decimal PlannedQuantity,
    string Decision,
    int? WorkId,
    string? WorkNumber,
    IReadOnlyList<ReplenishmentWorkPlanLineDto> Lines);

public sealed record ReplenishmentGenerationResultDto(
    int PoliciesExamined,
    int SignalsExamined,
    int WorkCreated,
    int WorkReused,
    int Blocked,
    IReadOnlyList<ReplenishmentWorkPlanDto> Plans);

public interface IReplenishmentExecutionService
{
    Task<Result<ReplenishmentGenerationResultDto>> GenerateAsync(
        ReplenishmentGenerationQuery query,
        string actorUserId,
        CancellationToken cancellationToken = default);
}
