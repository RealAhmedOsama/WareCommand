using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Packing;
using Wms.Application.Shipping;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Shipping;

namespace Wms.Infrastructure.Tests.Shipping;

public sealed class ShipmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly ShipmentService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _packingLocation;
    private readonly Customer _customer;

    public ShipmentServiceTests()
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

        _warehouse = new Warehouse("SHIP-WH", "Shipping Warehouse");
        _item = new Item("SHIP-ITEM", "Shipping Item", "EA");
        _availableStatus = new InventoryStatus(
            "SHIP_AVAILABLE",
            "Shipping available",
            "متاح للشحن",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _customer = new Customer("SHIP-CUST", "Shipping Customer", allowPartialShipment: true);
        _context.AddRange(_warehouse, _item, _availableStatus, _customer);
        _context.SaveChanges();

        _packingLocation = new Location(
            "SHIP-PACK",
            "Shipping packing location",
            _warehouse.Id,
            type: LocationType.Packing,
            isPickable: false,
            isReceivable: false);
        _context.Locations.Add(_packingLocation);
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _access.Object,
            _clock);
        _service = new ShipmentService(
            _context,
            _unitOfWork,
            _access.Object,
            _audit.Object,
            _ledger,
            _clock,
            NullLogger<ShipmentService>.Instance);
    }

    [Fact]
    public async Task LoadAndConfirm_IsAtomicAndIdempotent_AndUpdatesTracking()
    {
        var scenario = await CreateScenarioAsync();
        var carrier = await CreateCarrierAsync();
        var shipment = await CreateShipmentAsync(scenario, carrier);
        var load = await _service.OpenLoadAsync(
            new ShipmentLoadInput(shipment.Id, null, "TRAILER-1", "ROUTE-1", "load-open-1"),
            "shipper-1");
        load.IsSuccess.Should().BeTrue(load.FirstError?.Message);
        load.Value.Loads.Should().ContainSingle();

        var loaded = await _service.LoadPackageAsync(
            new ShipmentPackageScanInput(
                shipment.Id,
                scenario.Package.Id,
                load.Value.Loads.Single().Id,
                "load-package-1"),
            "shipper-1");
        loaded.IsSuccess.Should().BeTrue(loaded.FirstError?.Message);
        loaded.Value.Status.Should().Be(ShipmentStatus.Loaded);

        var confirmed = await _service.ConfirmShipmentAsync(
            new ShipmentCommandInput(shipment.Id, "ship-confirm-1"),
            "shipper-1");
        confirmed.IsSuccess.Should().BeTrue(confirmed.FirstError?.Message);
        confirmed.Value.Status.Should().Be(ShipmentStatus.Shipped);
        confirmed.Value.Packages.Single().Status.Should().Be(ShipmentPackageLinkStatus.Shipped);

        var replay = await _service.ConfirmShipmentAsync(
            new ShipmentCommandInput(shipment.Id, "ship-confirm-1"),
            "shipper-1");
        replay.IsSuccess.Should().BeTrue(replay.FirstError?.Message);
        (await _context.ShipmentCommands.CountAsync(command => command.Operation == "confirm"))
            .Should().Be(1);
        (await _context.Movements.CountAsync(movement => movement.Type == MovementType.Ship))
            .Should().Be(1);
        (await _context.Stock.CountAsync(stock => stock.LicensePlateId == scenario.Plate.Id))
            .Should().Be(0);
        (await _context.LicensePlates.SingleAsync(plate => plate.Id == scenario.Plate.Id))
            .Status.Should().Be(LicensePlateStatus.Shipped);
        (await _context.SalesOrderLines.SingleAsync(line => line.Id == scenario.Line.Id))
            .ShippedBaseQuantity.Should().Be(2m);
        (await _context.SalesOrders.SingleAsync(order => order.Id == scenario.Order.Id))
            .Status.Should().Be(SalesOrderStatus.Shipped);

        var tracking = await _service.UpdateTrackingAsync(
            new ShipmentTrackingUpdateInput(
                shipment.Id,
                "DELIVERED",
                "TRACK-1",
                "manual",
                null,
                _clock.UtcNow.UtcDateTime,
                null,
                "tracking-1"),
            "shipper-1");
        tracking.IsSuccess.Should().BeTrue(tracking.FirstError?.Message);
        tracking.Value.Status.Should().Be(ShipmentStatus.Delivered);
        tracking.Value.TrackingEvents.Should().ContainSingle();
    }

    [Fact]
    public async Task WrongPackageScanIsRejected_AndUnloadIsRequiredBeforeCancel()
    {
        var scenario = await CreateScenarioAsync();
        var carrier = await CreateCarrierAsync();
        var shipment = await CreateShipmentAsync(scenario, carrier);
        var opened = await _service.OpenLoadAsync(
            new ShipmentLoadInput(shipment.Id, null, "TRAILER-2", "ROUTE-2", "load-open-2"),
            "shipper-1");
        var loadId = opened.Value.Loads.Single().Id;

        var wrongPackage = await _service.LoadPackageAsync(
            new ShipmentPackageScanInput(shipment.Id, scenario.Package.Id + 999, loadId, "wrong-package-1"),
            "shipper-1");
        wrongPackage.IsSuccess.Should().BeFalse();
        wrongPackage.ErrorCode.Should().Be("shipping.package_load_failed");

        (await _service.LoadPackageAsync(
                new ShipmentPackageScanInput(shipment.Id, scenario.Package.Id, loadId, "load-package-2"),
                "shipper-1"))
            .IsSuccess.Should().BeTrue();

        var cancelledBeforeUnload = await _service.CancelShipmentAsync(
            new ShipmentCommandInput(shipment.Id, "cancel-before-unload-1", "operator correction"),
            "shipper-1");
        cancelledBeforeUnload.IsSuccess.Should().BeFalse();

        var unloaded = await _service.UnloadPackageAsync(
            new ShipmentPackageScanInput(shipment.Id, scenario.Package.Id, loadId, "unload-package-1"),
            "shipper-1");
        unloaded.IsSuccess.Should().BeTrue(unloaded.FirstError?.Message);
        unloaded.Value.Status.Should().Be(ShipmentStatus.Loading);

        var cancelled = await _service.CancelShipmentAsync(
            new ShipmentCommandInput(shipment.Id, "cancel-after-unload-1", "operator correction"),
            "shipper-1");
        cancelled.IsSuccess.Should().BeTrue(cancelled.FirstError?.Message);
        cancelled.Value.Status.Should().Be(ShipmentStatus.Cancelled);
        (await _context.Stock.CountAsync(stock => stock.LicensePlateId == scenario.Plate.Id))
            .Should().Be(1);
    }

    private async Task<CarrierDto> CreateCarrierAsync()
    {
        var carrier = await _service.CreateCarrierAsync(
            new CarrierInput("SHIP-CARRIER", "Shipping Carrier"),
            "admin-1");
        carrier.IsSuccess.Should().BeTrue(carrier.FirstError?.Message);
        var service = await _service.CreateCarrierServiceAsync(
            new CarrierServiceInput(carrier.Value.Id, "GROUND", "Ground", SupportsTracking: true),
            "admin-1");
        service.IsSuccess.Should().BeTrue(service.FirstError?.Message);
        return carrier.Value with { Services = [service.Value] };
    }

    private async Task<ShipmentDto> CreateShipmentAsync(Scenario scenario, CarrierDto carrier)
    {
        var result = await _service.CreateShipmentAsync(
            new ShipmentCreateInput(
                $"SHIP-{Guid.NewGuid():N}"[..20],
                _warehouse.Id,
                [scenario.Package.Id],
                carrier.Id,
                carrier.Services.Single().Id,
                IdempotencyKey: $"create-{Guid.NewGuid():N}"),
            "shipper-1");
        result.IsSuccess.Should().BeTrue(result.FirstError?.Message);
        result.Value.Status.Should().Be(ShipmentStatus.Ready);
        return result.Value;
    }

    private async Task<Scenario> CreateScenarioAsync()
    {
        var order = new SalesOrder(
            $"SO-SHIP-{Guid.NewGuid():N}"[..20],
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
            "SHIP-CARRIER",
            "GROUND",
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
            2m,
            _item.UnitOfMeasure,
            2m,
            1m,
            0,
            "EA",
            string.Empty);
        order.ReplaceDraftLines([line]);
        order.Confirm("creator-1", _clock.UtcNow.UtcDateTime);
        _context.SalesOrders.Add(order);
        await _context.SaveChangesAsync();
        line.RecordAllocation(2m);
        line.RecordPicked(2m);
        line.RecordPacked(2m);

        var plate = new LicensePlate(
            $"SHIP-LPN-{Guid.NewGuid():N}"[..20],
            LicensePlateType.Tote,
            _warehouse.Id,
            _packingLocation.Id);
        _context.LicensePlates.Add(plate);
        await _context.SaveChangesAsync();

        var station = new PackingStation("SHIP-STATION", "Shipping packing station", _warehouse.Id, _packingLocation.Id);
        _context.PackingStations.Add(station);
        await _context.SaveChangesAsync();
        var session = new PackingSession(
            $"PACK-SHIP-{Guid.NewGuid():N}"[..25],
            _warehouse.Id,
            station.Id,
            PackingSourceType.SalesOrder,
            order.DocumentNumber,
            order.Id,
            null,
            "packer-1",
            _clock.UtcNow.UtcDateTime);
        _context.PackingSessions.Add(session);
        await _context.SaveChangesAsync();

        var package = new ShipmentPackage(
            $"PKG-SHIP-{Guid.NewGuid():N}"[..25],
            _warehouse.Id,
            session.Id,
            plate.Id,
            PackingPackageType.Carton,
            order.Id,
            false,
            null,
            0m,
            0m,
            labelReference: "SHIP-LABEL");
        session.AddPackage(package);
        _context.ShipmentPackages.Add(package);
        await _context.SaveChangesAsync();
        var content = new ShipmentPackageContent(
            package.Id,
            line.Id,
            _item.Id,
            2m,
            _item.UnitOfMeasure,
            _packingLocation.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            null);
        package.AddContent(content);
        _context.ShipmentPackageContents.Add(content);
        _context.LicensePlateContents.Add(new LicensePlateContent(
            plate.Id,
            _item.Id,
            new Quantity(2m),
            inventoryStatusId: _availableStatus.Id));
        _context.Stock.Add(new Stock(
            _item.Id,
            _packingLocation.Id,
            new Quantity(2m),
            inventoryStatusId: _availableStatus.Id,
            licensePlateId: plate.Id));
        package.Close("packer-1", _clock.UtcNow.UtcDateTime, 2m, 20m, 20m, 20m);
        plate.Close();
        await _context.SaveChangesAsync();
        return new Scenario(order, line, package, plate);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed record Scenario(
        SalesOrder Order,
        SalesOrderLine Line,
        ShipmentPackage Package,
        LicensePlate Plate);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
