// Wms.Domain/Repositories/IUnitOfWork.cs

namespace Wms.Domain.Repositories;

public interface IUnitOfWork : IDisposable
{
    IItemRepository Items { get; }
    ILocationRepository Locations { get; }
    IReceivingRepository Receiving { get; }
    ILotRepository Lots { get; }
    ISerialNumberRepository SerialNumbers { get; }
    IInventoryStatusRepository InventoryStatuses { get; }
    ILicensePlateRepository LicensePlates { get; }
    IInventoryBalanceRepository InventoryBalances { get; }
    IInventoryTransactionRepository InventoryTransactions { get; }
    IInventoryCommandIdempotencyRepository InventoryCommandIdempotencies { get; }
    IInventoryReservationRepository InventoryReservations { get; }
    IStockRepository Stock { get; }
    IMovementRepository Movements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
