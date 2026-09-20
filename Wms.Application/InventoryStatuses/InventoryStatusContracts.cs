using Wms.Application.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Application.InventoryStatuses;

public enum InventoryStatusOperation
{
    Allocate = 1,
    Pick = 2,
    Ship = 3,
    Count = 4
}

public sealed record InventoryStatusDto(
    int Id,
    string Code,
    string Name,
    string LocalizedName,
    int? WarehouseId,
    string? WarehouseCode,
    bool IsAvailable,
    bool IsAllocatable,
    bool IsPickable,
    bool IsShippable,
    bool IsCountable,
    bool IsActive,
    bool IsSystem,
    LocationType? DefaultLocationType,
    bool ForceForLocationType);

public sealed record InventoryStatusDefinitionInput(
    string Code,
    string Name,
    string LocalizedName,
    int? WarehouseId,
    bool IsAvailable,
    bool IsAllocatable,
    bool IsPickable,
    bool IsShippable,
    bool IsCountable,
    LocationType? DefaultLocationType,
    bool ForceForLocationType);

public sealed record InventoryStatusTransitionDto(
    int Id,
    int FromStatusId,
    string FromStatusCode,
    int ToStatusId,
    string ToStatusCode,
    bool RequiresReason,
    bool IsActive);

public sealed record InventoryStatusTransitionInput(
    int FromStatusId,
    int ToStatusId,
    bool RequiresReason = true);

public sealed record InventoryStatusChangeResult(
    int SourceStockId,
    int? DestinationStockId,
    int OutboundMovementId,
    int InboundMovementId,
    decimal Quantity,
    InventoryStatusDto FromStatus,
    InventoryStatusDto ToStatus,
    string ReferenceNumber);

public interface IInventoryStatusService
{
    Task<Result<IReadOnlyList<InventoryStatusDto>>> GetStatusesAsync(
        int? warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatusDto>> CreateAsync(
        InventoryStatusDefinitionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatusDto>> UpdateAsync(
        int statusId,
        InventoryStatusDefinitionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatusDto>> SetActiveAsync(
        int statusId,
        bool isActive,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatusTransitionDto>> ConfigureTransitionAsync(
        InventoryStatusTransitionInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatusChangeResult>> ChangeStockStatusAsync(
        int stockId,
        decimal quantity,
        int targetStatusId,
        string reason,
        string? referenceNumber,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatus>> ResolveInboundStatusAsync(
        Location location,
        bool qualityInspectionRequired,
        CancellationToken cancellationToken = default);

    Task<Result<InventoryStatus>> ResolveDestinationStatusAsync(
        InventoryStatus sourceStatus,
        Location destination,
        CancellationToken cancellationToken = default);

    Task<Result> ValidateOperationAsync(
        Stock stock,
        InventoryStatusOperation operation,
        CancellationToken cancellationToken = default);
}
