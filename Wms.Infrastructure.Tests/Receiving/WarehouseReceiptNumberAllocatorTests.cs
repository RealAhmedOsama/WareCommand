using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Receiving;

namespace Wms.Infrastructure.Tests.Receiving;

public sealed class WarehouseReceiptNumberAllocatorTests
{
    [Fact]
    public async Task AllocateAsync_CommitsEachNumberInOrder()
    {
        var connectionString = $"Data Source=receipt_sequence_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();

        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(connectionString)
            .Options;
        int warehouseId;
        await using (var setup = new WmsDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse("SEQ-WH", "Sequence Warehouse");
            setup.Warehouses.Add(warehouse);
            await setup.SaveChangesAsync();
            warehouseId = warehouse.Id;
            setup.WarehouseNumberSequences.Add(new WarehouseNumberSequence(warehouse.Id));
            await setup.SaveChangesAsync();
        }

        var allocator = new WarehouseReceiptNumberAllocator(
            new FixedClock(new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero)),
            new SqliteWmsDbContextFactory(options));

        Assert.Equal(1, await allocator.AllocateAsync(warehouseId));
        Assert.Equal(2, await allocator.AllocateAsync(warehouseId));

        await using var verification = new WmsDbContext(options);
        var sequence = await verification.WarehouseNumberSequences
            .AsNoTracking()
            .SingleAsync(value => value.WarehouseId == warehouseId);
        Assert.Equal(3, sequence.NextReceiptNumber);
    }

    private sealed class SqliteWmsDbContextFactory(DbContextOptions<WmsDbContext> options)
        : IDbContextFactory<WmsDbContext>
    {
        public WmsDbContext CreateDbContext() => new(options);

        public Task<WmsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
