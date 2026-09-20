using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Repositories;

public sealed class MovementRepositoryTests : IDisposable
{
    private readonly WmsDbContext _context;
    private readonly MovementRepository _repository;

    public MovementRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new WmsDbContext(options);

        var access = new Mock<IWarehouseAccessService>();
        access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _repository = new MovementRepository(_context, access.Object);
    }

    [Fact]
    public async Task DateRangeIncludesStartAndExcludesNextBoundary()
    {
        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        var item = new Item("WIDGET-001", "Widget", "EA");
        _context.AddRange(warehouse, item);
        await _context.SaveChangesAsync();

        var location = new Location("RECEIVE", "Receiving", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        var fromUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        var toExclusiveUtc = fromUtc.AddDays(1);
        _context.Movements.AddRange(
            Movement.CreateReceipt(
                item.Id,
                location.Id,
                new Quantity(1),
                "user",
                timestampUtc: fromUtc),
            Movement.CreateReceipt(
                item.Id,
                location.Id,
                new Quantity(2),
                "user",
                timestampUtc: toExclusiveUtc),
            Movement.CreateReceipt(
                item.Id,
                location.Id,
                new Quantity(3),
                "user",
                timestampUtc: toExclusiveUtc.AddTicks(1)));
        await _context.SaveChangesAsync();

        var result = await _repository.GetByDateRangeAsync(fromUtc, toExclusiveUtc);

        result.Should().ContainSingle();
        result.Single().Timestamp.Should().Be(fromUtc);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
