using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class InventoryCommandIdempotencyRepository(WmsDbContext context)
    : IInventoryCommandIdempotencyRepository
{
    public Task<InventoryCommandIdempotency?> GetAsync(
        string callerScope,
        string commandKey,
        CancellationToken cancellationToken = default) =>
        context.InventoryCommandIdempotencies.SingleOrDefaultAsync(
            command => command.CallerScope == callerScope &&
                       command.CommandKey == commandKey,
            cancellationToken);

    public Task<InventoryCommandIdempotency?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        context.InventoryCommandIdempotencies.SingleOrDefaultAsync(
            command => command.Id == id,
            cancellationToken);

    public async Task<InventoryCommandIdempotency> AddAsync(
        InventoryCommandIdempotency command,
        CancellationToken cancellationToken = default)
    {
        var entry = await context.InventoryCommandIdempotencies.AddAsync(command, cancellationToken);
        return entry.Entity;
    }

    public Task<int> PruneAsync(
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default)
    {
        var inProgress = (int)Wms.Domain.Enums.InventoryCommandIdempotencyStatus.InProgress;
        return context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"InventoryCommandIdempotencies\" WHERE \"ExpiresAtUtc\" <= {beforeUtc} AND \"Status\" <> {inProgress}",
            cancellationToken);
    }
}
