using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Receiving;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Receiving;

public sealed class ReceivingRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SqlCommandCaptureInterceptor _commandCapture = new();
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly WmsDbContext _context;
    private readonly ReceivingRepository _repository;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _location;

    public ReceivingRepositoryTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_commandCapture)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess.Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));

        _warehouse = new Warehouse("RECEIVING-REPO", "Receiving Repository Warehouse");
        _item = new Item("RECEIVING-REPO-ITEM", "Receiving Repository Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();

        _location = new Location(
            "RECEIVING-REPO-DOCK",
            "Receiving Repository Dock",
            _warehouse.Id);
        _context.Locations.Add(_location);
        _context.SaveChanges();

        _repository = new ReceivingRepository(_context, _warehouseAccess.Object);
    }

    [Fact]
    public async Task GetTargetLoadsItemAndVisibleLocationInOneDatabaseCommand()
    {
        _commandCapture.Commands.Clear();

        var target = await _repository.GetTargetAsync(
            "receiving-repo-item",
            " receiving-repo-dock ");

        target.Should().NotBeNull();
        target!.Item.Id.Should().Be(_item.Id);
        target.Location!.Id.Should().Be(_location.Id);
        _commandCapture.Commands.Should().ContainSingle()
            .Which.Should().Contain("ReceivingRepository.GetTarget");
    }

    [Fact]
    public async Task GetTargetSelectsOneMatchingLocationWhenCodesRepeatAcrossWarehouses()
    {
        var otherWarehouse = new Warehouse("RECEIVING-REPO-OTHER", "Other Receiving Repository Warehouse");
        _context.Warehouses.Add(otherWarehouse);
        _context.SaveChanges();
        var duplicateCodeLocation = new Location(
            _location.Code,
            "Duplicate Receiving Repository Dock",
            otherWarehouse.Id);
        _context.Locations.Add(duplicateCodeLocation);
        _context.SaveChanges();

        var target = await _repository.GetTargetAsync(_item.Sku, _location.Code);

        target.Should().NotBeNull();
        target!.Location!.Id.Should().Be(_location.Id);
    }

    [Fact]
    public async Task GetTargetHidesLocationOutsideTheWarehouseScope()
    {
        _warehouseAccess.Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(false, new HashSet<int>()));

        var target = await _repository.GetTargetAsync(
            _item.Sku,
            _location.Code);

        target.Should().NotBeNull();
        target!.Item.Id.Should().Be(_item.Id);
        target.Location.Should().BeNull();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class SqlCommandCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }
}
