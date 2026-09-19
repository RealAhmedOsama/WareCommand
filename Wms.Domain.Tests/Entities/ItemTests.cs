// Wms.Domain.Tests/Entities/ItemTests.cs

using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.Entities;

public class ItemTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesItem()
    {
        // Arrange
        var sku = "WIDGET-001";
        var name = "Widget Type A";
        var unitOfMeasure = "EA";

        // Act
        var item = new Item(sku, name, unitOfMeasure);

        // Assert
        item.Sku.Should().Be(sku);
        item.Name.Should().Be(name);
        item.UnitOfMeasure.Should().Be(unitOfMeasure);
        item.IsActive.Should().BeTrue();
        item.RequiresLot.Should().BeFalse();
        item.RequiresSerial.Should().BeFalse();
        item.Barcodes.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithLotRequirement_SetsRequiresLotTrue()
    {
        // Arrange & Act
        var item = new Item("LOT-001", "Lot Controlled Item", "EA", true);

        // Assert
        item.RequiresLot.Should().BeTrue();
        item.RequiresSerial.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithSerialRequirement_SetsRequiresSerialTrue()
    {
        // Arrange & Act
        var item = new Item("SERIAL-001", "Serial Controlled Item", "EA", requiresSerial: true);

        // Assert
        item.RequiresSerial.Should().BeTrue();
        item.RequiresLot.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_WithInvalidSku_ThrowsArgumentException(string? invalidSku)
    {
        // Arrange & Act & Assert
        var act = () => new Item(invalidSku!, "Valid Name", "EA");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_WithInvalidName_ThrowsArgumentException(string? invalidName)
    {
        // Arrange & Act & Assert
        var act = () => new Item("VALID-SKU", invalidName!, "EA");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddBarcode_WithValidBarcode_AddsBarcodeToCollection()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");
        var barcode = new Barcode("123456789012");

        // Act
        item.AddBarcode(barcode);

        // Assert
        item.Barcodes.Should().Contain(barcode);
        item.Barcodes.Count.Should().Be(1);
    }

    [Fact]
    public void AddBarcode_WithDuplicateBarcode_ThrowsInvalidOperationException()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");
        var barcode = new Barcode("123456789012");
        item.AddBarcode(barcode);

        // Act & Assert
        var act = () => item.AddBarcode(barcode);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public void RemoveBarcode_WithExistingBarcode_RemovesBarcodeFromCollection()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");
        var barcode = new Barcode("123456789012");
        item.AddBarcode(barcode);

        // Act
        item.RemoveBarcode(barcode);

        // Assert
        item.Barcodes.Should().NotContain(barcode);
        item.Barcodes.Should().BeEmpty();
    }

    [Fact]
    public void RemoveBarcode_WithNonExistingBarcode_ThrowsInvalidOperationException()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");
        var barcode = new Barcode("123456789012");

        // Act & Assert
        var act = () => item.RemoveBarcode(barcode);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public void UpdateDetails_WithValidParameters_UpdatesItemDetails()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Old Name", "EA");
        var newName = "New Widget Name";
        var newDescription = "Updated description";

        // Act
        item.UpdateDetails(newName, newDescription);

        // Assert
        item.Name.Should().Be(newName);
        item.Description.Should().Be(newDescription);
        item.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void SetShelfLife_WithValidDays_UpdatesShelfLifeDays()
    {
        // Arrange
        var item = new Item("PERISHABLE-001", "Perishable Item", "EA", true);
        var shelfLifeDays = 30;

        // Act
        item.SetShelfLife(shelfLifeDays);

        // Assert
        item.ShelfLifeDays.Should().Be(shelfLifeDays);
        item.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void SetShelfLife_WithNegativeDays_ThrowsArgumentException(int invalidDays)
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");

        // Act & Assert
        var act = () => item.SetShelfLife(invalidDays);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_WhenActive_SetsIsActiveFalse()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");
        item.IsActive.Should().BeTrue(); // Precondition

        // Act
        item.Deactivate();

        // Assert
        item.IsActive.Should().BeFalse();
        item.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Activate_WhenInactive_SetsIsActiveTrue()
    {
        // Arrange
        var item = new Item("WIDGET-001", "Widget", "EA");
        item.Deactivate(); // Make inactive first
        item.IsActive.Should().BeFalse(); // Precondition

        // Act
        item.Activate();

        // Assert
        item.IsActive.Should().BeTrue();
        item.UpdatedAt.Should().NotBeNull();
    }
}
