using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.Application.Warehouses;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Warehouses;

namespace Wms.Infrastructure.Tests.Warehouses;

public sealed class WarehouseManagementServiceTests : IDisposable
{
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly WarehouseManagementService _service;

    public WarehouseManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new WmsDbContext(options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new WarehouseManagementService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<WarehouseManagementService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_CreatesIndependentWarehouseAndNumberSequence()
    {
        var first = await _service.CreateAsync(CreateRequest("MAIN", "Main"));
        var second = await _service.CreateAsync(CreateRequest("SECOND", "Second"));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.Id.Should().NotBe(second.Value.Id);
        (await _context.WarehouseNumberSequences.CountAsync()).Should().Be(2);
        (await _service.ListAsync()).Value.Should().HaveCount(2);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.WarehouseCreated),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ConfigureAndEnableWorkflows_RequiresSameWarehouseActiveLocations()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        var otherWarehouse = new Warehouse("OTHER", "Other");
        _context.Warehouses.AddRange(warehouse, otherWarehouse);
        await _context.SaveChangesAsync();

        var locations = Warehouse.RequiredOperationalLocationRoles
            .Select((role, index) => new Location($"L-{index}", role.ToString(), warehouse.Id))
            .ToArray();
        var otherLocation = new Location("OTHER-LOC", "Other", otherWarehouse.Id);
        _context.Locations.AddRange(locations);
        _context.Locations.Add(otherLocation);
        await _context.SaveChangesAsync();

        var incomplete = await _service.EnableWorkflowsAsync(warehouse.Id);
        incomplete.IsFailure.Should().BeTrue();
        incomplete.ErrorCode.Should().Be("warehouse.operational_locations_incomplete");

        var selections = Warehouse.RequiredOperationalLocationRoles
            .Select((role, index) => new WarehouseOperationalLocationSelection(role, locations[index].Id))
            .ToList();
        selections[0] = new WarehouseOperationalLocationSelection(
            WarehouseOperationalLocationRole.Receiving,
            otherLocation.Id);
        var invalid = await _service.ConfigureOperationalLocationsAsync(warehouse.Id, selections);
        invalid.IsFailure.Should().BeTrue();
        invalid.ErrorCode.Should().Be("warehouse.operational_location_invalid");

        selections[0] = new WarehouseOperationalLocationSelection(
            WarehouseOperationalLocationRole.Receiving,
            locations[0].Id);
        var configured = await _service.ConfigureOperationalLocationsAsync(warehouse.Id, selections);
        configured.IsSuccess.Should().BeTrue();

        var enabled = await _service.EnableWorkflowsAsync(warehouse.Id);
        enabled.IsSuccess.Should().BeTrue();
        (await _context.Warehouses.FindAsync(warehouse.Id))!.WorkflowEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAsync_BlocksWarehouseWithAvailableOrReservedStock()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        var item = new Item("ITEM-1", "Item", "EA");
        _context.Warehouses.Add(warehouse);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var location = new Location("STOCK", "Stock", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        _context.Stock.Add(new Stock(item.Id, location.Id, new Quantity(5)));
        await _context.SaveChangesAsync();

        var result = await _service.DeactivateAsync(warehouse.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("warehouse.deactivation_blocked");
        (await _context.Warehouses.FindAsync(warehouse.Id))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateAsync_PreservesWarehouseAndDisablesWorkflowsWhenSafe()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        warehouse.EnableWorkflows(Warehouse.RequiredOperationalLocationRoles);
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var result = await _service.DeactivateAsync(warehouse.Id);

        result.IsSuccess.Should().BeTrue();
        var saved = await _context.Warehouses.FindAsync(warehouse.Id);
        saved!.IsActive.Should().BeFalse();
        saved.WorkflowEnabled.Should().BeFalse();
    }

    private static CreateWarehouseRequest CreateRequest(string code, string name) =>
        new(
            code,
            name,
            ArabicName: null,
            Address: null,
            ContactName: null,
            ContactPhone: null,
            ContactEmail: null,
            TimeZone: "UTC",
            AllowNegativeStock: false,
            RequireLocationForAdjustment: true,
            BlockExpiredReceipt: true,
            ExpiryWarningDays: 30);

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
