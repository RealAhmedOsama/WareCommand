using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.SalesOrders;

public sealed record SalesOrderAllocationCommand(
    IReadOnlyList<int>? LineIds = null,
    bool ReleaseToWarehouse = true,
    bool Replan = false,
    bool Simulation = false,
    string IdempotencyKey = "",
    string? Reason = null);

public sealed record SalesOrderAllocationBatchCommand(
    IReadOnlyList<int> SalesOrderIds,
    bool ReleaseToWarehouse = true,
    bool Replan = false,
    string IdempotencyKey = "",
    string? Reason = null);

public sealed record SalesOrderAllocationSelectionDto(
    int ReservationAllocationId,
    int LocationId,
    int? LotId,
    int? SerialNumberId,
    string? SerialNumber,
    int? LicensePlateId,
    int InventoryStatusId,
    decimal Quantity,
    string Status,
    string? Reason);

public sealed record SalesOrderAllocationLineDto(
    int LineId,
    int LineNumber,
    int ItemId,
    string ItemSku,
    decimal OrderedBaseQuantity,
    decimal AllocatedBaseQuantity,
    decimal BackorderBaseQuantity,
    decimal CancelledBaseQuantity,
    int? ReservationId,
    string? ReservationStatus,
    decimal ActiveReservedQuantity,
    decimal ConsumedQuantity,
    string Explanation,
    IReadOnlyList<SalesOrderAllocationSelectionDto> Selections);

public sealed record SalesOrderAllocationWorkDto(
    int WorkId,
    string WorkNumber,
    int LineId,
    decimal PlannedQuantity,
    string Status,
    string CreationKey);

public sealed record SalesOrderAllocationResultDto(
    int SalesOrderId,
    string DocumentNumber,
    int WarehouseId,
    SalesOrderStatus OrderStatus,
    bool IsSimulation,
    bool IsFullyAllocated,
    decimal OrderedBaseQuantity,
    decimal AllocatedBaseQuantity,
    decimal BackorderBaseQuantity,
    IReadOnlyList<SalesOrderAllocationLineDto> Lines,
    IReadOnlyList<SalesOrderAllocationWorkDto> Work,
    string Explanation);

public sealed record SalesOrderAllocationBatchItemDto(
    int SalesOrderId,
    string? DocumentNumber,
    SalesOrderAllocationResultDto? Result,
    string? ErrorCode,
    string? Error);

public sealed record SalesOrderAllocationBatchResultDto(
    IReadOnlyList<SalesOrderAllocationBatchItemDto> Items,
    int TotalOrders,
    int FullyAllocatedOrders,
    int PartialOrders,
    int FailedOrders);

public interface ISalesOrderAllocationService
{
    Task<Result<SalesOrderAllocationResultDto>> GetAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationResultDto>> AllocateAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationResultDto>> SimulateAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationResultDto>> ReleaseAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationResultDto>> ReallocateAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationResultDto>> UnreleaseAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationResultDto>> CancelAsync(
        int salesOrderId,
        SalesOrderAllocationCommand command,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<SalesOrderAllocationBatchResultDto>> AllocateBatchAsync(
        SalesOrderAllocationBatchCommand command,
        string userId,
        CancellationToken cancellationToken = default);
}
