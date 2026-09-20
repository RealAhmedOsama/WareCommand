// Wms.Infrastructure/Repositories/UnitOfWork.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Wms.Application.Identity;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly WmsDbContext _context;
    private IDbContextTransaction? _transaction;

    public UnitOfWork(WmsDbContext context, IWarehouseAccessService warehouseAccessService)
    {
        _context = context;
        Items = new ItemRepository(context);
        Locations = new LocationRepository(context, warehouseAccessService);
        Lots = new LotRepository(context);
        Stock = new StockRepository(context, warehouseAccessService);
        Movements = new MovementRepository(context, warehouseAccessService);
    }

    public IItemRepository Items { get; }
    public ILocationRepository Locations { get; }
    public ILotRepository Lots { get; }
    public IStockRepository Stock { get; }
    public IMovementRepository Movements { get; }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational())
        {
            return;
        }

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
