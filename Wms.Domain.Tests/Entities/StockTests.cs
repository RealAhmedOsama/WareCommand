// Wms.Domain.Tests/Entities/StockTests.cs

using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.Entities;

public class StockTests
{
    [Fact]
    public void Constructor_WithValidParameters_CreatesStock()
    {
        // Arrange
        var itemId = 1;
        var locationId = 1;
        var quantity = new Quantity(10.0m);

        // Act
        var stock = new Stock(itemId, locationId, quantity);

        // Assert
        stock.ItemId.Should().Be(itemId);
        stock.LocationId.Should().Be(locationId);
        stock.QuantityAvailable.Should().Be(quantity);
        stock.QuantityReserved.Should().Be(new Quantity(0));
        stock.LotId.Should().BeNull();
        stock.SerialNumber.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithLotAndSerial_SetsOptionalProperties()
    {
        // Arrange
        var itemId = 1;
        var locationId = 1;
        var quantity = new Quantity(5.0m);
        var lotId = 100;
        var serialNumber = "SN-001";

        // Act
        var stock = new Stock(itemId, locationId, quantity, lotId, serialNumber);

        // Assert
        stock.LotId.Should().Be(lotId);
        stock.SerialNumber.Should().Be(serialNumber);
    }

    [Fact]
    public void AddQuantity_WithValidQuantity_IncreasesAvailableQuantity()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var addQuantity = new Quantity(5.0m);
        var expectedQuantity = new Quantity(15.0m);

        // Act
        stock.AddQuantity(addQuantity);

        // Assert
        stock.QuantityAvailable.Should().Be(expectedQuantity);
        stock.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void RemoveQuantity_WithValidQuantity_DecreasesAvailableQuantity()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var removeQuantity = new Quantity(3.0m);
        var expectedQuantity = new Quantity(7.0m);

        // Act
        stock.RemoveQuantity(removeQuantity);

        // Assert
        stock.QuantityAvailable.Should().Be(expectedQuantity);
        stock.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void RemoveQuantity_WithQuantityGreaterThanAvailable_ThrowsInvalidOperationException()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(5.0m));
        var removeQuantity = new Quantity(10.0m);

        // Act & Assert
        var act = () => stock.RemoveQuantity(removeQuantity);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot remove more quantity than available*");
    }

    [Fact]
    public void ReserveQuantity_WithValidQuantity_IncreasesReservedQuantity()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var reserveQuantity = new Quantity(4.0m);

        // Act
        stock.ReserveQuantity(reserveQuantity);

        // Assert
        stock.QuantityReserved.Should().Be(reserveQuantity);
        stock.QuantityAvailable.Should().Be(new Quantity(10.0m)); // Available unchanged
        stock.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void ReserveQuantity_WithQuantityGreaterThanAvailable_ThrowsInvalidOperationException()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(5.0m));
        var reserveQuantity = new Quantity(10.0m);

        // Act & Assert
        var act = () => stock.ReserveQuantity(reserveQuantity);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot reserve more quantity than available*");
    }

    [Fact]
    public void ReleaseReservation_WithValidQuantity_DecreasesReservedQuantity()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var reserveQuantity = new Quantity(4.0m);
        var releaseQuantity = new Quantity(2.0m);
        stock.ReserveQuantity(reserveQuantity);

        // Act
        stock.ReleaseReservation(releaseQuantity);

        // Assert
        stock.QuantityReserved.Should().Be(new Quantity(2.0m));
        stock.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void ReleaseReservation_WithQuantityGreaterThanReserved_ThrowsInvalidOperationException()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var reserveQuantity = new Quantity(2.0m);
        var releaseQuantity = new Quantity(5.0m);
        stock.ReserveQuantity(reserveQuantity);

        // Act & Assert
        var act = () => stock.ReleaseReservation(releaseQuantity);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot release more than reserved*");
    }

    [Fact]
    public void GetAvailableQuantity_ReturnsAvailableMinusReserved()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var reserveQuantity = new Quantity(3.0m);
        stock.ReserveQuantity(reserveQuantity);

        // Act
        var availableQuantity = stock.GetAvailableQuantity();

        // Assert
        availableQuantity.Should().Be(new Quantity(7.0m)); // 10 - 3 = 7
    }

    [Fact]
    public void AdjustQuantity_WithNewQuantity_UpdatesAvailableQuantity()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var newQuantity = new Quantity(15.0m);
        var reason = "Physical count adjustment";

        // Act
        stock.AdjustQuantity(newQuantity, reason);

        // Assert
        stock.QuantityAvailable.Should().Be(newQuantity);
        stock.UpdatedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AdjustQuantity_WithInvalidReason_ThrowsArgumentException(string? invalidReason)
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var newQuantity = new Quantity(15.0m);

        // Act & Assert
        var act = () => stock.AdjustQuantity(newQuantity, invalidReason!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AdjustQuantity_WithNegativeQuantity_ThrowsArgumentException()
    {
        // Arrange
        var stock = new Stock(1, 1, new Quantity(10.0m));
        var reason = "Valid reason";

        // Act & Assert
        var act = () => stock.AdjustQuantity(new Quantity(-5.0m), reason);
        act.Should().Throw<ArgumentException>();
    }
}
