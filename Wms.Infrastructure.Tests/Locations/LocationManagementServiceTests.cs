using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Locations;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Locations;

namespace Wms.Infrastructure.Tests.Locations;

public sealed class LocationManagementServiceTests : IDisposable
{
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly LocationManagementService _service;

    public LocationManagementServiceTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new WmsDbContext(options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new LocationManagementService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<LocationManagementService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_AllowsSameCodeAcrossWarehousesButNotWithinOne()
    {
        var firstWarehouse = new Warehouse("MAIN", "Main");
        var secondWarehouse = new Warehouse("SECOND", "Second");
        _context.Warehouses.AddRange(firstWarehouse, secondWarehouse);
        await _context.SaveChangesAsync();

        var first = await _service.CreateAsync(CreateRequest("BIN-01", firstWarehouse.Id), "user-1");
        var second = await _service.CreateAsync(CreateRequest("bin-01", secondWarehouse.Id), "user-1");
        var duplicate = await _service.CreateAsync(CreateRequest("BIN-01", firstWarehouse.Id), "user-1");

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        duplicate.IsFailure.Should().BeTrue();
        duplicate.ErrorCode.Should().Be("location.code_conflict");
        (await _context.Locations.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_RejectsCrossWarehouseParent()
    {
        var firstWarehouse = new Warehouse("MAIN", "Main");
        var secondWarehouse = new Warehouse("SECOND", "Second");
        _context.Warehouses.AddRange(firstWarehouse, secondWarehouse);
        await _context.SaveChangesAsync();
        var parent = new Location("PARENT", "Parent", secondWarehouse.Id);
        _context.Locations.Add(parent);
        await _context.SaveChangesAsync();

        var result = await _service.CreateAsync(
            CreateRequest("CHILD", firstWarehouse.Id, parentLocationId: parent.Id),
            "user-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("location.parent_warehouse_mismatch");
    }

    [Fact]
    public async Task UpdateAsync_RejectsCycleAndBlocksRestructureWithStock()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        var item = new Item("ITEM-1", "Item", "EA");
        _context.Warehouses.Add(warehouse);
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var parent = new Location("PARENT", "Parent", warehouse.Id);
        _context.Locations.Add(parent);
        await _context.SaveChangesAsync();
        var child = new Location("CHILD", "Child", warehouse.Id, parent.Id);
        _context.Locations.Add(child);
        await _context.SaveChangesAsync();

        var cycle = await _service.UpdateAsync(
            UpdateRequest(parent.Id, child.Id),
            "user-1");
        cycle.IsFailure.Should().BeTrue();
        cycle.ErrorCode.Should().Be("location.hierarchy_cycle");

        _context.Stock.Add(new Stock(item.Id, child.Id, new Wms.Domain.ValueObjects.Quantity(2)));
        await _context.SaveChangesAsync();
        var restructure = await _service.UpdateAsync(
            UpdateRequest(child.Id, null, type: LocationType.Bulk, isPickable: false, isReceivable: true),
            "user-1");

        restructure.IsFailure.Should().BeTrue();
        restructure.ErrorCode.Should().Be("location.restructure_blocked");
    }

    [Fact]
    public async Task ListAsync_UsesStablePagingAndFiltersByType()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        for (var index = 1; index <= 3; index++)
        {
            var location = new Location(
                $"BIN-{index:00}",
                $"Bin {index}",
                warehouse.Id,
                type: LocationType.Bin,
                isPickable: true,
                isReceivable: false);
            _context.Locations.Add(location);
        }

        _context.Locations.Add(new Location(
            "DOCK-01",
            "Dock",
            warehouse.Id,
            type: LocationType.Dock,
            isPickable: false,
            isReceivable: true));
        await _context.SaveChangesAsync();

        var result = await _service.ListAsync(new LocationListQuery(
            warehouse.Id,
            Type: LocationType.Bin,
            Page: 2,
            PageSize: 2));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(3);
        result.Value.TotalPages.Should().Be(2);
        result.Value.Items.Should().ContainSingle()
            .Which.Code.Should().Be("BIN-03");
    }

    [Fact]
    public async Task GenerateAndImportAsync_AreAuditedAndValidateParents()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var generated = await _service.GenerateAsync(
            new BulkLocationGenerationRequest(
                warehouse.Id,
                "R-",
                "Rack",
                1,
                2,
                2,
                null,
                LocationType.Rack,
                IsPickable: false,
                IsReceivable: false,
                IsCountable: true,
                MaxUnits: 100,
                StorageProfile: "Standard"),
            "user-1");
        generated.IsSuccess.Should().BeTrue();
        generated.Value.Should().HaveCount(2);

        var imported = await _service.ImportAsync(
            warehouse.Id,
            "CODE,NAME,PARENT_CODE,TYPE,BARCODE,PRIORITY,IS_PICKABLE,IS_RECEIVABLE,IS_COUNTABLE\nZONE-01,Zone,,Zone,,0,true,false,true\nBIN-01,Bin,ZONE-01,Bin,,0,true,false,true",
            "user-1");
        imported.IsSuccess.Should().BeTrue();
        imported.Value.CreatedCount.Should().Be(2);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.LocationBulkGenerated),
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.LocationBulkImported),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static LocationCreateRequest CreateRequest(
        string code,
        int warehouseId,
        int? parentLocationId = null) =>
        new(
            code,
            code,
            warehouseId,
            parentLocationId,
            LocationType.Storage,
            null,
            0,
            true,
            true,
            true,
            true,
            true,
            1000,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}");

    private static LocationUpdateRequest UpdateRequest(
        int id,
        int? parentLocationId,
        LocationType type = LocationType.Storage,
        bool isPickable = true,
        bool isReceivable = true) =>
        new(
            id,
            "Updated",
            parentLocationId,
            type,
            null,
            0,
            isPickable,
            isReceivable,
            true,
            true,
            true,
            1000,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}");
}
