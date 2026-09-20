using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.InventoryStatuses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.InventoryStatuses;

namespace Wms.Infrastructure.Tests.InventoryStatuses;

public sealed class InventoryStatusServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly InventoryStatusService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _location;

    public InventoryStatusServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new WmsDbContext(options);
        _context.Database.EnsureCreated();

        var access = new Mock<IWarehouseAccessService>();
        access.Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _unitOfWork = new UnitOfWork(_context, access.Object);
        _service = new InventoryStatusService(
            _unitOfWork,
            _auditWriter.Object,
            new SystemClock(),
            NullLogger<InventoryStatusService>.Instance);

        _warehouse = new Warehouse("TEST", "Test Warehouse");
        _item = new Item("STATUS-001", "Status Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();
        _location = new Location("Z001", "Storage", _warehouse.Id);
        _context.Locations.Add(_location);
        _context.SaveChanges();
    }

    [Fact]
    public async Task ChangeStockStatus_SplitsQuantityAndWritesBalancedStatusLegs()
    {
        var stock = new Stock(
            _item.Id,
            _location.Id,
            new Quantity(10m),
            inventoryStatusId: InventoryStatusSystemIds.Available);
        _context.Stock.Add(stock);
        await _context.SaveChangesAsync();

        var result = await _service.ChangeStockStatusAsync(
            stock.Id,
            3m,
            InventoryStatusSystemIds.Hold,
            "Quality review",
            "STATUS-001",
            "user-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        var balances = await _context.Stock
            .Where(entity => entity.ItemId == _item.Id && entity.LocationId == _location.Id)
            .OrderBy(entity => entity.InventoryStatusId)
            .ToListAsync();
        balances.Should().HaveCount(2);
        balances.Single(entity => entity.InventoryStatusId == InventoryStatusSystemIds.Available)
            .QuantityAvailable.Value.Should().Be(7m);
        balances.Single(entity => entity.InventoryStatusId == InventoryStatusSystemIds.Hold)
            .QuantityAvailable.Value.Should().Be(3m);

        var movements = await _context.Movements
            .Where(movement => movement.Type == MovementType.StatusChange)
            .ToListAsync();
        movements.Should().HaveCount(2);
        movements.Should().OnlyContain(movement => movement.Quantity.Value == 3m);
        movements.Should().ContainSingle(movement =>
            movement.StatusChangeLeg == InventoryStatusMovementLeg.Outbound &&
            movement.InventoryStatusId == InventoryStatusSystemIds.Available);
        movements.Should().ContainSingle(movement =>
            movement.StatusChangeLeg == InventoryStatusMovementLeg.Inbound &&
            movement.InventoryStatusId == InventoryStatusSystemIds.Hold);
        movements.Should().OnlyContain(movement =>
            movement.FromInventoryStatusId == InventoryStatusSystemIds.Available &&
            movement.ToInventoryStatusId == InventoryStatusSystemIds.Hold);

        _auditWriter.Verify(writer => writer.RecordAsync(
                It.Is<AuditRecord>(record =>
                    record.Action == WmsAuditActions.InventoryStatusChanged &&
                    record.EntityType == WmsAuditEntityTypes.Stock),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChangeStockStatus_RejectsPartialSerialIdentity()
    {
        var stock = new Stock(
            _item.Id,
            _location.Id,
            new Quantity(1m),
            serialNumber: "SN-001",
            inventoryStatusId: InventoryStatusSystemIds.Available);
        _context.Stock.Add(stock);
        await _context.SaveChangesAsync();

        var result = await _service.ChangeStockStatusAsync(
            stock.Id,
            .5m,
            InventoryStatusSystemIds.Hold,
            "Quality review",
            null,
            "user-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("inventory_status.serial_partial_forbidden");
        (await _context.Movements.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ValidateOperation_BlocksPickFromHold()
    {
        var stock = new Stock(
            _item.Id,
            _location.Id,
            new Quantity(4m),
            inventoryStatusId: InventoryStatusSystemIds.Hold);
        _context.Stock.Add(stock);
        await _context.SaveChangesAsync();
        var loaded = await _unitOfWork.Stock.GetByIdAsync(stock.Id);

        var result = await _service.ValidateOperationAsync(
            loaded!,
            InventoryStatusOperation.Pick);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("inventory_status.pick_blocked");
    }

    [Fact]
    public async Task SetActive_RejectsDisablingStatusInUse()
    {
        var created = await _service.CreateAsync(
            new InventoryStatusDefinitionInput(
                "CUSTOM_HOLD",
                "Custom Hold",
                "تعليق مخصص",
                _warehouse.Id,
                false,
                false,
                false,
                false,
                true,
                null,
                false),
            "user-1");
        created.IsSuccess.Should().BeTrue(created.Error);

        _context.Stock.Add(new Stock(
            _item.Id,
            _location.Id,
            new Quantity(2m),
            inventoryStatusId: created.Value.Id));
        await _context.SaveChangesAsync();

        var result = await _service.SetActiveAsync(created.Value.Id, false, "user-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("inventory_status.in_use");
    }

    [Fact]
    public async Task ResolveInboundStatus_UsesQualityAndForcedLocationRules()
    {
        var quality = await _service.ResolveInboundStatusAsync(_location, qualityInspectionRequired: true);
        quality.IsSuccess.Should().BeTrue(quality.Error);
        quality.Value.Code.Should().Be(InventoryStatusCodes.QualityPending);

        var quarantineLocation = new Location(
            "Q001",
            "Quarantine",
            _warehouse.Id,
            type: LocationType.Quarantine,
            isPickable: false,
            isReceivable: false);
        _context.Locations.Add(quarantineLocation);
        await _context.SaveChangesAsync();

        var forced = await _service.ResolveInboundStatusAsync(
            quarantineLocation,
            qualityInspectionRequired: false);
        forced.IsSuccess.Should().BeTrue(forced.Error);
        forced.Value.Code.Should().Be(InventoryStatusCodes.Quarantine);
    }

    [Fact]
    public async Task ChangeStockStatus_ReleasesQualityPendingStockThroughCatalogTransition()
    {
        var stock = new Stock(
            _item.Id,
            _location.Id,
            new Quantity(5m),
            inventoryStatusId: InventoryStatusSystemIds.QualityPending);
        _context.Stock.Add(stock);
        await _context.SaveChangesAsync();

        var result = await _service.ChangeStockStatusAsync(
            stock.Id,
            5m,
            InventoryStatusSystemIds.Available,
            "QC passed",
            "QC-001",
            "user-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        (await _context.Stock.SingleAsync(entity =>
                entity.ItemId == _item.Id &&
                entity.InventoryStatusId == InventoryStatusSystemIds.Available))
            .QuantityAvailable.Value.Should().Be(5m);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
