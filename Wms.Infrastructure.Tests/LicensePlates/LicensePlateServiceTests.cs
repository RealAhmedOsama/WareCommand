using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.LicensePlates;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.LicensePlates;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.LicensePlates;

public sealed class LicensePlateServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly Mock<IClock> _clock = new();
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly LicensePlateService _service;
    private readonly Warehouse _warehouse;
    private readonly Location _firstLocation;
    private readonly Location _secondLocation;
    private readonly Item _item;

    public LicensePlateServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clock
            .SetupGet(clock => clock.UtcNow)
            .Returns(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        _unitOfWork = new UnitOfWork(_context, _warehouseAccess.Object);
        _service = new LicensePlateService(
            _unitOfWork,
            _auditWriter.Object,
            _clock.Object,
            NullLogger<LicensePlateService>.Instance);

        _warehouse = new Warehouse("LP-WH", "License Plate Warehouse");
        _item = new Item("LP-ITEM", "License Plate Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();

        _firstLocation = new Location("LP-A", "License Plate A", _warehouse.Id);
        _secondLocation = new Location("LP-B", "License Plate B", _warehouse.Id);
        _context.AddRange(_firstLocation, _secondLocation);
        _context.SaveChanges();
    }

    [Fact]
    public async Task NumberingCreatesUniqueGeneratedNumbersAndHistory()
    {
        var configured = await _service.ConfigureNumberingAsync(
            new LicensePlateNumberingInput(_warehouse.Id, "PAL-", 6, 42),
            "user-1");

        configured.IsSuccess.Should().BeTrue(configured.Error);
        configured.Value.Prefix.Should().Be("PAL-");
        configured.Value.NextNumber.Should().Be(42);

        var created = await _service.CreateAsync(
            new LicensePlateCreateInput(
                _warehouse.Id,
                _firstLocation.Id,
                Number: null,
                IsSscc: false,
                LicensePlateType.Pallet),
            "user-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.Number.Should().Be("PAL-000042");
        (await _context.LicensePlateHistories.CountAsync(history =>
            history.LicensePlateId == created.Value.Id &&
            history.Action == LicensePlateHistoryAction.Created)).Should().Be(1);
    }

    [Fact]
    public async Task ContentReceiptAndPartialMoveReconcileStockContentAndMovement()
    {
        var source = await CreatePlateAsync("SOURCE");
        var target = await CreatePlateAsync("TARGET", _secondLocation.Id);

        var received = await _service.AddContentAsync(
            source.Id,
            new LicensePlateContentInput(_item.Id, 10m),
            "user-1",
            "PO-1");
        received.IsSuccess.Should().BeTrue(received.Error);

        var moved = await _service.MoveContentAsync(
            source.Id,
            target.Id,
            new LicensePlateContentInput(_item.Id, 3m),
            "user-1",
            "MOVE-1");
        moved.IsSuccess.Should().BeTrue(moved.Error);

        var sourceContent = await _context.LicensePlateContents
            .SingleAsync(content => content.LicensePlateId == source.Id);
        var targetContent = await _context.LicensePlateContents
            .SingleAsync(content => content.LicensePlateId == target.Id);
        sourceContent.Quantity.Value.Should().Be(7m);
        targetContent.Quantity.Value.Should().Be(3m);

        var balances = await _context.Stock
            .Where(stock => stock.LicensePlateId != null)
            .OrderBy(stock => stock.LocationId)
            .ToListAsync();
        balances.Should().HaveCount(2);
        balances.Single(stock => stock.LicensePlateId == source.Id).QuantityAvailable.Value.Should().Be(7m);
        balances.Single(stock => stock.LicensePlateId == target.Id).QuantityAvailable.Value.Should().Be(3m);

        var movements = await _context.Movements
            .Where(movement => movement.LicensePlateId != null)
            .OrderBy(movement => movement.Id)
            .ToListAsync();
        movements.Should().HaveCount(2);
        movements[0].Type.Should().Be(MovementType.Receipt);
        movements[1].Type.Should().Be(MovementType.Transfer);
        movements[1].FromLicensePlateId.Should().Be(source.Id);
        movements[1].ToLicensePlateId.Should().Be(target.Id);

        (await _context.LicensePlateHistories.CountAsync(history =>
            history.Action == LicensePlateHistoryAction.ContentMoved)).Should().Be(2);
    }

    [Fact]
    public async Task NestingRejectsCyclesAndClosedPlatesCannotReceiveContent()
    {
        var parent = await CreatePlateAsync("PARENT");
        var child = await CreatePlateAsync("CHILD");

        var nested = await _service.NestAsync(child.Id, parent.Id, "user-1");
        nested.IsSuccess.Should().BeTrue(nested.Error);

        var cycle = await _service.NestAsync(parent.Id, child.Id, "user-1");
        cycle.IsFailure.Should().BeTrue();
        cycle.ErrorCode.Should().Be("license_plate.rule_violation");

        var packed = await _service.PackAsync(child.Id, "user-1", "PACK-1");
        packed.IsSuccess.Should().BeTrue(packed.Error);

        var content = await _service.AddContentAsync(
            child.Id,
            new LicensePlateContentInput(_item.Id, 1m),
            "user-1");
        content.IsFailure.Should().BeTrue();
        content.ErrorCode.Should().Be("license_plate.rule_violation");
    }

    [Fact]
    public async Task MovingRootMovesTrackedDescendantsAndTheirStockBalances()
    {
        var parent = await CreatePlateAsync("MOVE-PARENT");
        var child = await CreatePlateAsync("MOVE-CHILD");
        (await _service.NestAsync(child.Id, parent.Id, "user-1")).IsSuccess.Should().BeTrue();
        (await _service.AddContentAsync(
            child.Id,
            new LicensePlateContentInput(_item.Id, 2m),
            "user-1")).IsSuccess.Should().BeTrue();

        var moved = await _service.MoveAsync(parent.Id, _secondLocation.Id, "user-1", "MOVE-ROOT");

        moved.IsSuccess.Should().BeTrue(moved.Error);
        var savedChild = await _context.LicensePlates
            .AsNoTracking()
            .SingleAsync(plate => plate.Id == child.Id);
        savedChild.CurrentLocationId.Should().Be(_secondLocation.Id);
        var savedStock = await _context.Stock.SingleAsync(stock => stock.LicensePlateId == child.Id);
        savedStock.LocationId.Should().Be(_secondLocation.Id);
        (await _context.Movements.CountAsync(movement => movement.Type == MovementType.Transfer))
            .Should().Be(1);
    }

    [Fact]
    public async Task SerialContentRequiresOneUnitAndUpdatesSerialPlacement()
    {
        var serialItem = new Item("LP-SERIAL", "Serial License Plate Item", "EA", requiresSerial: true);
        _context.Add(serialItem);
        await _context.SaveChangesAsync();
        var serial = new SerialNumber("LP-SN-1", serialItem.Id);
        _context.Add(serial);
        await _context.SaveChangesAsync();
        var plate = await CreatePlateAsync("SERIAL");

        var invalid = await _service.AddContentAsync(
            plate.Id,
            new LicensePlateContentInput(serialItem.Id, 2m, SerialNumberId: serial.Id),
            "user-1");
        invalid.IsFailure.Should().BeTrue();

        var valid = await _service.AddContentAsync(
            plate.Id,
            new LicensePlateContentInput(serialItem.Id, 1m, SerialNumberId: serial.Id),
            "user-1");
        valid.IsSuccess.Should().BeTrue(valid.Error);

        var savedSerial = await _context.SerialNumbers.SingleAsync(entity => entity.Id == serial.Id);
        savedSerial.CurrentLocationId.Should().Be(_firstLocation.Id);
        savedSerial.CurrentLicensePlateId.Should().Be(plate.Id);
    }

    [Fact]
    public async Task PackedPlateShipsAndReturnsWithContentReconciled()
    {
        var plate = await CreatePlateAsync("RETURNABLE");
        var added = await _service.AddContentAsync(
            plate.Id,
            new LicensePlateContentInput(_item.Id, 4m),
            "user-1");
        added.IsSuccess.Should().BeTrue(added.Error);

        var packed = await _service.PackAsync(plate.Id, "user-1");
        packed.IsSuccess.Should().BeTrue(packed.Error);
        var shipped = await _service.ShipAsync(plate.Id, "user-1", "SHIP-1");
        shipped.IsSuccess.Should().BeTrue(shipped.Error);
        (await _context.Stock.CountAsync(stock => stock.LicensePlateId == plate.Id)).Should().Be(0);

        var returned = await _service.ReturnAsync(
            plate.Id,
            _secondLocation.Id,
            "user-1",
            "RMA-1");
        returned.IsSuccess.Should().BeTrue(returned.Error);
        returned.Value.Status.Should().Be(LicensePlateStatus.Returned);
        (await _context.Stock.SingleAsync(stock => stock.LicensePlateId == plate.Id))
            .QuantityAvailable.Value.Should().Be(4m);
        (await _context.LicensePlateHistories.CountAsync(history =>
            history.LicensePlateId == plate.Id &&
            history.Action == LicensePlateHistoryAction.Returned)).Should().Be(1);
    }

    [Fact]
    public async Task InvalidSsccIsReturnedAsValidationFailure()
    {
        var result = await _service.CreateAsync(
            new LicensePlateCreateInput(
                _warehouse.Id,
                _firstLocation.Id,
                "000123456789012344",
                IsSscc: true,
                LicensePlateType.Pallet),
            "user-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("license_plate.invalid");
        (await _context.LicensePlates.CountAsync()).Should().Be(0);
    }

    private async Task<LicensePlate> CreatePlateAsync(
        string suffix,
        int? locationId = null)
    {
        var result = await _service.CreateAsync(
            new LicensePlateCreateInput(
                _warehouse.Id,
                locationId ?? _firstLocation.Id,
                $"LPN-{suffix}",
                IsSscc: false,
                LicensePlateType.Pallet),
            "user-1");
        result.IsSuccess.Should().BeTrue(result.Error);
        return await _context.LicensePlates.SingleAsync(plate => plate.Id == result.Value.Id);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
