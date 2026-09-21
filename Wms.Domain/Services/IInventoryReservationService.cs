using Wms.Domain.Entities;
using Wms.Domain.Inventory;

namespace Wms.Domain.Services;

public interface IInventoryReservationService
{
    Task<InventoryReservationResult> ReserveAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationSimulationResult> SimulateAsync(
        InventoryReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationResult> ReleaseAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationResult> ConsumeAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationResult> CancelAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationResult> ReallocateAsync(
        InventoryReservationMutationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryReservationResult>> ExpireAsync(
        DateTime? nowUtc = null,
        string actorUserId = "system",
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationResult?> GetAsync(
        int reservationId,
        CancellationToken cancellationToken = default);

    Task<InventoryReservationResult?> GetByDemandAsync(
        string demandType,
        string demandId,
        int? demandLine,
        int warehouseId,
        CancellationToken cancellationToken = default);
}
