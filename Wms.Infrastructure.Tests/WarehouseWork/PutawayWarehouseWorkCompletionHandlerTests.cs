using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.WarehouseWork;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.WarehouseWork;

public sealed class PutawayWarehouseWorkCompletionHandlerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IStockMovementService> _stockMovements = new();
    private readonly UnitOfWork _unitOfWork;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _source;
    private readonly Location _destination;
    private readonly WarehouseWorkEntity _work;
    private readonly WarehouseWorkLine _line;

    public PutawayWarehouseWorkCompletionHandlerTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _unitOfWork = new UnitOfWork(_context, _access.Object);

        _warehouse = new Warehouse("PUT-WH", "Putaway Warehouse");
        _item = new Item("PUT-ITEM", "Putaway Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();

        _source = new Location(
            "PUT-RECEIVE",
            "Putaway receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false);
        _destination = new Location(
            "PUT-STORAGE",
            "Putaway storage",
            _warehouse.Id,
            type: LocationType.Storage,
            isPickable: true);
        _context.AddRange(_source, _destination);
        _context.SaveChanges();

        _work = new WarehouseWorkEntity(
            "WORK-PUTAWAY-1",
            "receipt:1:line:1:movement:1",
            WarehouseWorkType.Putaway,
            _warehouse.Id,
            "ReceiptLine",
            "1",
            notes: "handler test");
        _work.MakeAvailable(DateTime.UtcNow);
        _line = new WarehouseWorkLine(
            1,
            _warehouse.Id,
            _item.Id,
            5m,
            "EA",
            _source.Id);
        _work.AddLine(_line);
        _context.Add(_work);
        _context.SaveChanges();

        _stockMovements
            .Setup(service => service.PutawayAsync(
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<Quantity>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int?>(),
                It.IsAny<InventoryOwnerKind>(),
                It.IsAny<int?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(Movement.CreatePutaway(
                _item.Id,
                _source.Id,
                _destination.Id,
                new Quantity(5m),
                "worker-1"));
    }

    [Fact]
    public async Task ValidScanDelegatesMovementAndReturnsActualQuantity()
    {
        var handler = new PutawayWarehouseWorkCompletionHandler(
            _stockMovements.Object,
            _unitOfWork);

        var result = await handler.ExecuteAsync(
            _work,
            new WarehouseWorkCompletionInput(
                "complete-1",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        _line.Id,
                        _item.Id,
                        _source.Id,
                        _destination.Id,
                        5m)
                ]),
            "worker-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.ActualLines.Should().ContainSingle(line =>
            line.LineId == _line.Id && line.ActualQuantity == 5m);
        _stockMovements.Verify(service => service.PutawayAsync(
            _item.Id,
            _source.Id,
            _destination.Id,
            It.Is<Quantity>(quantity => quantity.Value == 5m),
            "worker-1",
            null,
            null,
            "WORK-PUTAWAY-1",
            null,
            It.IsAny<CancellationToken>(),
            null,
            It.IsAny<InventoryOwnerKind>(),
            It.IsAny<int?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task ItemMismatchIsRejectedBeforeMovement()
    {
        var handler = new PutawayWarehouseWorkCompletionHandler(
            _stockMovements.Object,
            _unitOfWork);

        var result = await handler.ExecuteAsync(
            _work,
            new WarehouseWorkCompletionInput(
                "complete-2",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        _line.Id,
                        _item.Id + 1,
                        _source.Id,
                        _destination.Id,
                        5m)
                ]),
            "worker-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("work.scan_item_mismatch");
        _stockMovements.Verify(service => service.PutawayAsync(
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<Quantity>(),
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<int?>()), Times.Never);
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
        _connection.Dispose();
    }
}
