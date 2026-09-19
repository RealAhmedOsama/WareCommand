// Wms.Infrastructure.Tests/Repositories/ItemRepositoryTests.cs

using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Repositories;

public class ItemRepositoryTests : IDisposable
{
    private readonly WmsDbContext _context;
    private readonly ItemRepository _repository;

    public ItemRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new WmsDbContext(options);
        _context.Database.EnsureCreated();
        _repository = new ItemRepository(_context);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetAllAsync_WithItems_ReturnsAllItems()
    {
        // Arrange
        var items = new[]
        {
            new Item("WIDGET-001", "Widget A", "EA"),
            new Item("GADGET-001", "Gadget B", "EA"),
            new Item("TOOL-001", "Tool C", "EA")
        };

        _context.Items.AddRange(items);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(i => i.Sku == "WIDGET-001");
        result.Should().Contain(i => i.Sku == "GADGET-001");
        result.Should().Contain(i => i.Sku == "TOOL-001");
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ReturnsItem()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget A", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByIdAsync(item.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Sku.Should().Be("WIDGET-001");
        result.Name.Should().Be("Widget A");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingId_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByIdAsync(999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetBySkuAsync_WithExistingSku_ReturnsItem()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget A", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetBySkuAsync("WIDGET-001");

        // Assert
        result.Should().NotBeNull();
        result!.Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task GetByBarcodeAsync_WithExistingBarcode_ReturnsItem()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget A", "EA");
        var barcode = new Barcode("123456789012");
        item.AddBarcode(barcode);

        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByBarcodeAsync(barcode);

        // Assert
        result.Should().NotBeNull();
        result!.Sku.Should().Be("WIDGET-001");
        result.Barcodes.Should().Contain(barcode);
    }

    [Fact]
    public async Task GetByBarcodeAsync_WithNonExistingBarcode_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByBarcodeAsync(new Barcode("999999999999"));

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_WithSkuMatch_ReturnsMatchingItems()
    {
        // Arrange
        var items = new[]
        {
            new Item("WIDGET-001", "Widget A", "EA"),
            new Item("WIDGET-002", "Widget B", "EA"),
            new Item("GADGET-001", "Gadget C", "EA")
        };

        _context.Items.AddRange(items);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.SearchAsync("WIDGET");

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(i => i.Sku.Contains("WIDGET"));
    }

    [Fact]
    public async Task SearchAsync_WithNameMatch_ReturnsMatchingItems()
    {
        // Arrange
        var items = new[]
        {
            new Item("ITEM-001", "Super Widget", "EA"),
            new Item("ITEM-002", "Ultra Widget", "EA"),
            new Item("ITEM-003", "Basic Tool", "EA")
        };

        _context.Items.AddRange(items);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.SearchAsync("Widget");

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(i => i.Name.Contains("Widget"));
    }

    [Fact]
    public async Task SearchAsync_WithBarcodeMatch_ReturnsMatchingItems()
    {
        // Arrange
        var item1 = new Item("WIDGET-001", "Widget A", "EA");
        var item2 = new Item("GADGET-001", "Gadget B", "EA");

        item1.AddBarcode(new Barcode("123456789012"));
        item2.AddBarcode(new Barcode("234567890123"));

        _context.Items.AddRange(item1, item2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.SearchAsync("123456");

        // Assert
        result.Should().HaveCount(1);
        result.First().Sku.Should().Be("WIDGET-001");
    }

    [Fact]
    public async Task AddAsync_WithNewItem_AddsToContext()
    {
        // Arrange
        var item = new Item("NEW-001", "New Item", "EA");

        // Act
        await _repository.AddAsync(item);
        await _context.SaveChangesAsync();

        // Assert
        var addedItem = await _context.Items.FirstOrDefaultAsync(i => i.Sku == "NEW-001");
        addedItem.Should().NotBeNull();
        addedItem!.Name.Should().Be("New Item");
    }

    [Fact]
    public async Task UpdateAsync_WithExistingItem_UpdatesProperties()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget A", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Act
        item.UpdateDetails("Updated Widget", "Updated description");
        await _repository.UpdateAsync(item);
        await _context.SaveChangesAsync();

        // Assert
        var updatedItem = await _context.Items.FirstOrDefaultAsync(i => i.Id == item.Id);
        updatedItem.Should().NotBeNull();
        updatedItem!.Name.Should().Be("Updated Widget");
        updatedItem.Description.Should().Be("Updated description");
        updatedItem.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithExistingItem_RemovesFromContext()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget A", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        // Act
        await _repository.DeleteAsync(item);
        await _context.SaveChangesAsync();

        // Assert
        var deletedItem = await _context.Items.FirstOrDefaultAsync(i => i.Id == item.Id);
        deletedItem.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveItemsAsync_ReturnsOnlyActiveItems()
    {
        // Arrange
        var activeItem = new Item("ACTIVE-001", "Active Item", "EA");
        var inactiveItem = new Item("INACTIVE-001", "Inactive Item", "EA");
        inactiveItem.Deactivate();

        _context.Items.AddRange(activeItem, inactiveItem);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetActiveItemsAsync();

        // Assert
        result.Should().HaveCount(1);
        result.First().Sku.Should().Be("ACTIVE-001");
        result.Should().OnlyContain(i => i.IsActive);
    }
}
