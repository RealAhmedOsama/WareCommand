// Wms.Domain/Repositories/IUnitOfWork.cs

namespace Wms.Domain.Repositories;

public interface IUnitOfWork : IDisposable
{
    IItemRepository Items { get; }
    ILocationRepository Locations { get; }
    ILotRepository Lots { get; }
    ISerialNumberRepository SerialNumbers { get; }
    IStockRepository Stock { get; }
    IMovementRepository Movements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
