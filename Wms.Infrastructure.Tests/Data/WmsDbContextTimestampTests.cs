using Wms.Application.Context;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Data;

public sealed class WmsDbContextTimestampTests : IDisposable
{
    private static readonly DateTime FixedUtc =
        new(2026, 9, 20, 12, 34, 56, DateTimeKind.Utc);

    private readonly WmsDbContext _context;

    public WmsDbContextTimestampTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new WmsDbContext(options, new FixedClock(new DateTimeOffset(FixedUtc)));
    }

    [Fact]
    public async Task AddedEntitiesAndMovementsUseTheInjectedUtcClock()
    {
        var item = new Item("WIDGET-001", "Widget", "EA");
        var movement = Movement.CreateReceipt(1, 1, new Quantity(1), "user");
        _context.AddRange(item, movement);

        await _context.SaveChangesAsync();

        item.CreatedAt.Should().Be(FixedUtc);
        movement.CreatedAt.Should().Be(FixedUtc);
        movement.Timestamp.Should().Be(FixedUtc);
    }

    [Fact]
    public async Task ModifiedEntitiesUseTheInjectedUtcClockForUpdatedAt()
    {
        var item = new Item("WIDGET-001", "Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        item.UpdateDetails("Updated Widget");
        await _context.SaveChangesAsync();

        item.UpdatedAt.Should().Be(FixedUtc);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
