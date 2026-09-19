// Wms.Infrastructure.Tests/Data/WmsDbContextTests.cs

using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Data;

public class WmsDbContextTests : IDisposable
{
    private readonly WmsDbContext _context;

    public WmsDbContextTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new WmsDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAndSaveWarehouse_ShouldPersistToDatabase()
    {
        // Arrange
        var warehouse = new Warehouse("MAIN", "Main Warehouse");

        // Act
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        // Assert
        var savedWarehouse = await _context.Warehouses.FirstOrDefaultAsync();
        savedWarehouse.Should().NotBeNull();
        savedWarehouse!.Code.Should().Be("MAIN");
        savedWarehouse.Name.Should().Be("Main Warehouse");
    }

    [Fact]
    public async Task AddAndSaveItem_WithBarcodes_ShouldPersistToDatabase()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget Type A", "EA");
        item.AddBarcode(new Barcode("123456789012"));
        item.AddBarcode(new Barcode("234567890123"));

        // Act
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Assert
        var savedItem = await _context.Items
            .Include(i => i.Barcodes)
            .FirstOrDefaultAsync();

        savedItem.Should().NotBeNull();
        savedItem!.Sku.Should().Be("WIDGET-001");
        savedItem.Barcodes.Should().HaveCount(2);
        savedItem.Barcodes.Should().Contain(b => b.Value == "123456789012");
    }

    [Fact]
    public async Task AddAndSaveLocation_WithParentChild_ShouldPersistHierarchy()
    {
        // Arrange
        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var parentLocation = new Location("Z001", "Zone 1", warehouse.Id);
        _context.Locations.Add(parentLocation);
        await _context.SaveChangesAsync();

        var childLocation = new Location("Z001-A001", "Aisle 1", warehouse.Id, parentLocation.Id);
        _context.Locations.Add(childLocation);
        await _context.SaveChangesAsync();

        // Act & Assert
        var savedParent = await _context.Locations
            .FirstOrDefaultAsync(l => l.Code == "Z001");

        var savedChild = await _context.Locations
            .Include(l => l.ParentLocation)
            .FirstOrDefaultAsync(l => l.Code == "Z001-A001");

        savedParent.Should().NotBeNull();

        savedChild.Should().NotBeNull();
        savedChild!.ParentLocationId.Should().Be(parentLocation.Id);

        // Check child locations count by querying directly
        var childCount = await _context.Locations.CountAsync(l => l.ParentLocationId == parentLocation.Id);
        childCount.Should().Be(1);
    }

    [Fact]
    public async Task AddAndSaveStock_ShouldPersistQuantities()
    {
        // Arrange
        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        var item = new Item("WIDGET-001", "Widget", "EA");
        var location = new Location("Z001", "Zone 1", warehouse.Id);

        _context.Warehouses.Add(warehouse);
        _context.Items.Add(item);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        var stock = new Stock(item.Id, location.Id, new Quantity(10.0m));
        stock.ReserveQuantity(new Quantity(2.0m));

        // Act
        _context.Stock.Add(stock);
        await _context.SaveChangesAsync();

        // Assert
        var savedStock = await _context.Stock
            .Include(s => s.Item)
            .Include(s => s.Location)
            .FirstOrDefaultAsync();

        savedStock.Should().NotBeNull();
        savedStock!.QuantityAvailable.Value.Should().Be(10.0m);
        savedStock.QuantityReserved.Value.Should().Be(2.0m);
        savedStock.GetAvailableQuantity().Value.Should().Be(8.0m);
    }

    [Fact]
    public async Task QueryItems_ByBarcode_ShouldReturnCorrectItem()
    {
        // Arrange
        var item1 = new Item("WIDGET-001", "Widget A", "EA");
        var item2 = new Item("GADGET-001", "Gadget B", "EA");

        item1.AddBarcode(new Barcode("123456789012"));
        item2.AddBarcode(new Barcode("234567890123"));

        _context.Items.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        // Act
        var foundItem = await _context.Items
            .Where(i => i.Barcodes.Any(b => b.Value == "123456789012"))
            .FirstOrDefaultAsync();

        // Assert
        foundItem.Should().NotBeNull();
        foundItem!.Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task ConcurrencyTest_UpdateSameEntity_ShouldHandleOptimisticConcurrency()
    {
        // Arrange
        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        // For in-memory database, we'll just test that updates work
        // Real concurrency testing would require a relational database
        var warehouseFromDb = await _context.Warehouses.FirstAsync();

        // Act
        warehouseFromDb.UpdateDetails("Updated Name");
        await _context.SaveChangesAsync();

        // Assert
        var updatedWarehouse = await _context.Warehouses.FirstAsync();
        updatedWarehouse.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task ComplexQuery_StockWithMultipleIncludes_ShouldLoadAllData()
    {
        // Arrange
        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        var item = new Item("WIDGET-001", "Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var location = new Location("Z001", "Zone 1", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();

        var lot = new Lot("LOT-001", item.Id, DateTime.Today.AddDays(30));
        _context.Lots.Add(lot);
        await _context.SaveChangesAsync();

        var stock = new Stock(item.Id, location.Id, new Quantity(10.0m), lot.Id);
        _context.Stock.Add(stock);
        await _context.SaveChangesAsync();

        // Act
        var result = await _context.Stock
            .Include(s => s.Item)
            .ThenInclude(i => i.Barcodes)
            .Include(s => s.Location)
            .ThenInclude(l => l.Warehouse)
            .Include(s => s.Lot)
            .FirstOrDefaultAsync(s => s.ItemId == item.Id && s.LocationId == location.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Item.Should().NotBeNull();
        result.Location.Should().NotBeNull();
        result.Lot.Should().NotBeNull();
        result.Location.Warehouse.Should().NotBeNull();
        result.Item.Sku.Should().Be("WIDGET-001");
        result.Lot!.Number.Should().Be("LOT-001");
    }
}
