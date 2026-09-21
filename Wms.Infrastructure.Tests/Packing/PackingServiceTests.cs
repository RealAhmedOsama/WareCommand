using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Packing;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Packing;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Packing;

public sealed class PackingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly PackingService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _source;
    private readonly Location _stationLocation;
    private readonly PackingStation _station;
    private readonly Customer _customer;

    public PackingServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("PACK-WH", "Packing Warehouse");
        _item = new Item("PACK-ITEM", "Packing Item", "EA");
        _item.UpdateMasterData(new ItemMasterDetails(
            "Packing Item",
            PurchaseUnit: "EA",
            SalesUnit: "EA",
            NetWeightKg: 1m));
        _availableStatus = new InventoryStatus(
            "PACK_AVAILABLE",
            "Packing available",
            "متاح للتعبئة",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _customer = new Customer("PACK-CUST", "Packing Customer", allowPartialShipment: true);
        _context.AddRange(_warehouse, _item, _availableStatus, _customer);
        _context.SaveChanges();

        _source = new Location(
            "PACK-STAGE",
            "Packing staging",
            _warehouse.Id,
            type: LocationType.Staging,
            isPickable: false);
        _stationLocation = new Location(
            "PACK-01",
            "Packing station 1",
            _warehouse.Id,
            type: LocationType.Packing,
            isPickable: false,
            isReceivable: false);
        _context.AddRange(_source, _stationLocation);
        _context.SaveChanges();

        _station = new PackingStation(
            "PACK-01",
            "Packing station 1",
            _warehouse.Id,
            _stationLocation.Id,
            supportedDevices: "scanner,scale",
            scaleProfile: "scale-1");
        _context.PackingStations.Add(_station);
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        _service = new PackingService(
            _context,
            _unitOfWork,
            _access.Object,
            _audit.Object,
            _ledger,
            _clock,
            NullLogger<PackingService>.Instance);
    }

    [Fact]
    public async Task Pack_IsIdempotent_ClosesWithWeightAndCompletesSession()
    {
        var scenario = await CreateScenarioAsync(5m, expectedWeightKg: 3m);

        var input = new PackingScanInput(
            scenario.Package.Id,
            scenario.Line.Id,
            _item.Id,
            _source.Id,
            3m,
            _availableStatus.Id,
            IdempotencyKey: "pack-1");

        var first = await _service.PackAsync(input, "packer-1");
        var replay = await _service.PackAsync(input, "packer-1");

        first.IsSuccess.Should().BeTrue(first.FirstError?.Message);
        replay.IsSuccess.Should().BeTrue(replay.FirstError?.Message);
        replay.Value.PackedQuantity.Should().Be(3m);

        (await _context.Stock
            .SingleAsync(stock => stock.ItemId == _item.Id && stock.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(2m);
        (await _context.Stock
            .SingleAsync(stock => stock.ItemId == _item.Id && stock.LocationId == _stationLocation.Id))
            .QuantityAvailable.Value.Should().Be(3m);
        (await _context.PackingCommands.CountAsync(command => command.Operation == "pack"))
            .Should().Be(1);

        var closed = await _service.ClosePackageAsync(
            new PackingPackageMeasureInput(
                scenario.Package.Id,
                "close-1",
                ActualWeightKg: 3m,
                LengthCm: 30m,
                WidthCm: 20m,
                HeightCm: 10m),
            "packer-1");

        closed.IsSuccess.Should().BeTrue(closed.FirstError?.Message);
        closed.Value.Status.Should().Be(ShipmentPackageStatus.Closed);
        closed.Value.VolumeCubicMeters.Should().Be(0.006m);

        var completed = await _service.CompleteSessionAsync(
            scenario.Session.Id,
            "complete-1",
            "packer-1");

        completed.IsSuccess.Should().BeTrue(completed.FirstError?.Message);
        completed.Value.Status.Should().Be(PackingSessionStatus.Completed);
        (await _context.LicensePlates.SingleAsync(plate => plate.Id == scenario.Plate.Id))
            .Status.Should().Be(LicensePlateStatus.Closed);
    }

    [Fact]
    public async Task Pack_RejectsWrongOrderOwner_AndCloseRejectsWeightVariance()
    {
        var scenario = await CreateScenarioAsync(5m, expectedWeightKg: 3m);
        var otherOrder = await CreateOrderAsync(5m);

        var wrongOwner = await _service.PackAsync(
            new PackingScanInput(
                scenario.Package.Id,
                otherOrder.Line.Id,
                _item.Id,
                _source.Id,
                1m,
                _availableStatus.Id,
                IdempotencyKey: "wrong-owner-1"),
            "packer-1");

        wrongOwner.IsSuccess.Should().BeFalse();
        wrongOwner.ErrorCode.Should().Be("packing.content_pack_failed");
        (await _context.Stock
            .SingleAsync(stock => stock.ItemId == _item.Id && stock.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(5m);

        var packed = await _service.PackAsync(
            new PackingScanInput(
                scenario.Package.Id,
                scenario.Line.Id,
                _item.Id,
                _source.Id,
                3m,
                _availableStatus.Id,
                IdempotencyKey: "pack-variance-1"),
            "packer-1");
        packed.IsSuccess.Should().BeTrue(packed.FirstError?.Message);

        var variance = await _service.ClosePackageAsync(
            new PackingPackageMeasureInput(
                scenario.Package.Id,
                "close-variance-1",
                ActualWeightKg: 5m),
            "packer-1");

        variance.IsSuccess.Should().BeFalse();
        variance.ErrorCode.Should().Be("packing.package_close_failed");
        (await _context.ShipmentPackages.SingleAsync(package => package.Id == scenario.Package.Id))
            .Status.Should().Be(ShipmentPackageStatus.Open);
    }

    [Fact]
    public async Task ClosedPackageRequiresReopenBeforeRemoval_ThenCanBeVoided()
    {
        var scenario = await CreateScenarioAsync(2m);
        var input = new PackingScanInput(
            scenario.Package.Id,
            scenario.Line.Id,
            _item.Id,
            _source.Id,
            2m,
            _availableStatus.Id,
            IdempotencyKey: "pack-remove-1");

        var packed = await _service.PackAsync(input, "packer-1");
        packed.IsSuccess.Should().BeTrue(packed.FirstError?.Message);
        var closed = await _service.ClosePackageAsync(
                new PackingPackageMeasureInput(scenario.Package.Id, "close-remove-1", ActualWeightKg: 2m),
                "packer-1");
        closed.IsSuccess.Should().BeTrue(closed.FirstError?.Message);

        var prematureRemove = await _service.RemoveContentAsync(
            input with { IdempotencyKey = "remove-before-reopen-1" },
            "packer-1");
        prematureRemove.IsSuccess.Should().BeFalse();
        prematureRemove.ErrorCode.Should().Be("packing.content_remove_failed");

        var reopened = await _service.ReopenPackageAsync(
            new PackingPackageCommandInput(scenario.Package.Id, "reopen-1", "weight correction"),
            "packer-1");
        reopened.IsSuccess.Should().BeTrue(reopened.FirstError?.Message);

        var removed = await _service.RemoveContentAsync(
            input with { IdempotencyKey = "remove-after-reopen-1" },
            "packer-1");
        removed.IsSuccess.Should().BeTrue(removed.FirstError?.Message);
        removed.Value.PackedQuantity.Should().Be(0m);

        var voided = await _service.VoidPackageAsync(
            new PackingPackageCommandInput(scenario.Package.Id, "void-1", "empty package correction"),
            "packer-1");
        voided.IsSuccess.Should().BeTrue(voided.FirstError?.Message);
        voided.Value.Status.Should().Be(ShipmentPackageStatus.Voided);

        (await _context.Stock
            .SingleAsync(stock => stock.ItemId == _item.Id && stock.LocationId == _source.Id))
            .QuantityAvailable.Value.Should().Be(2m);
        (await _context.LicensePlates.SingleAsync(plate => plate.Id == scenario.Plate.Id))
            .Status.Should().Be(LicensePlateStatus.Voided);
        (await _context.SalesOrderLines.SingleAsync(line => line.Id == scenario.Line.Id))
            .PackedBaseQuantity.Should().Be(0m);
    }

    private async Task<Scenario> CreateScenarioAsync(
        decimal sourceQuantity,
        decimal? expectedWeightKg = null)
    {
        var order = await CreateOrderAsync(5m);
        var plate = new LicensePlate(
            $"PACK-LPN-{Guid.NewGuid():N}"[..20],
            LicensePlateType.Tote,
            _warehouse.Id,
            _stationLocation.Id);
        _context.LicensePlates.Add(plate);
        _context.Stock.Add(new Stock(
            _item.Id,
            _source.Id,
            new Quantity(sourceQuantity),
            inventoryStatusId: _availableStatus.Id));
        await _context.SaveChangesAsync();

        var sessionResult = await _service.StartSessionAsync(
            new PackingSessionStartInput(
                $"PACK-SESSION-{Guid.NewGuid():N}"[..30],
                _warehouse.Id,
                _station.Id,
                PackingSourceType.SalesOrder,
                order.Order.DocumentNumber,
                order.Order.Id,
                IdempotencyKey: "session-1"),
            "packer-1");
        sessionResult.IsSuccess.Should().BeTrue(sessionResult.FirstError?.Message);

        var packageResult = await _service.CreatePackageAsync(
            new PackingPackageCreateInput(
                sessionResult.Value.Id,
                $"PACK-PKG-{Guid.NewGuid():N}"[..28],
                plate.Id,
                PackingPackageType.Carton,
                order.Order.Id,
                ExpectedWeightKg: expectedWeightKg,
                WeightToleranceKg: 0.1m,
                LabelReference: "LBL-PACK-1",
                IdempotencyKey: "package-1"),
            "packer-1");
        packageResult.IsSuccess.Should().BeTrue(packageResult.FirstError?.Message);

        return new Scenario(order.Order, order.Line, sessionResult.Value, packageResult.Value, plate);
    }

    private async Task<(SalesOrder Order, SalesOrderLine Line)> CreateOrderAsync(decimal quantity)
    {
        var order = new SalesOrder(
            $"SO-PACK-{Guid.NewGuid():N}"[..20],
            _warehouse.Id,
            _warehouse.Code,
            _customer.Id,
            _customer.Code,
            _customer.LegalName,
            _customer.LocalizedName,
            _customer.ContactName,
            _customer.ContactEmail,
            _customer.ContactPhone,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 21),
            null,
            null,
            "MANUAL",
            null,
            10,
            null,
            null,
            null,
            null,
            true,
            null,
            "creator-1");
        var line = new SalesOrderLine(
            1,
            _item.Id,
            _item.Sku,
            _item.Name,
            _item.LocalizedName,
            null,
            _item.UnitOfMeasure,
            quantity,
            _item.UnitOfMeasure,
            quantity,
            1m,
            0,
            "EA",
            string.Empty);
        order.ReplaceDraftLines([line]);
        order.Confirm("creator-1", _clock.UtcNow.UtcDateTime);
        _context.SalesOrders.Add(order);
        await _context.SaveChangesAsync();

        line.RecordAllocation(quantity);
        line.RecordPicked(quantity);
        await _context.SaveChangesAsync();
        return (order, line);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed record Scenario(
        SalesOrder Order,
        SalesOrderLine Line,
        PackingSessionDto Session,
        ShipmentPackageDto Package,
        LicensePlate Plate);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
