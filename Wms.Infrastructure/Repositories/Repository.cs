// Wms.Infrastructure/Repositories/Repository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Domain.Common;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class Repository<T> : IRepository<T> where T : Entity
{
    protected DbSet<T> DbSet { get; }

    public Repository(WmsDbContext context)
    {
        DbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync(new object[] { id }, cancellationToken);
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet.ToListAsync(cancellationToken);
    }

    public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        var entry = await DbSet.AddAsync(entity, cancellationToken);
        return entry.Entity;
    }

    public virtual Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public virtual Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        DbSet.Remove(entity);
        return Task.CompletedTask;
    }
}
