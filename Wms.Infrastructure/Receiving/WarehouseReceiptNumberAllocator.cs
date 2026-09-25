using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Receiving;

public sealed class WarehouseReceiptNumberAllocator(
    IClock clock,
    IDbContextFactory<WmsDbContext> contextFactory)
{
    public async Task<long> AllocateAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var updatedAt = clock.UtcNow.UtcDateTime;
        var rowsUpdated = await context.WarehouseNumberSequences
            .Where(sequence => sequence.WarehouseId == warehouseId &&
                               sequence.NextReceiptNumber < long.MaxValue)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(sequence => sequence.NextReceiptNumber, sequence => sequence.NextReceiptNumber + 1)
                .SetProperty(sequence => sequence.Revision, sequence => sequence.Revision + 1)
                .SetProperty(sequence => sequence.UpdatedAt, updatedAt),
                cancellationToken);

        long allocatedNumber;
        if (rowsUpdated == 1)
        {
            var nextNumber = await context.WarehouseNumberSequences.AsNoTracking()
                .Where(sequence => sequence.WarehouseId == warehouseId)
                .Select(sequence => sequence.NextReceiptNumber)
                .SingleAsync(cancellationToken);
            allocatedNumber = nextNumber - 1;
        }
        else
        {
            var currentNumber = await context.WarehouseNumberSequences.AsNoTracking()
                .Where(sequence => sequence.WarehouseId == warehouseId)
                .Select(sequence => (long?)sequence.NextReceiptNumber)
                .SingleOrDefaultAsync(cancellationToken);
            if (currentNumber.HasValue)
            {
                throw new InvalidOperationException("The warehouse receipt number sequence is exhausted.");
            }

            var sequence = new WarehouseNumberSequence(warehouseId);
            allocatedNumber = sequence.AllocateReceiptNumber();
            context.WarehouseNumberSequences.Add(sequence);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return allocatedNumber;
    }
}
