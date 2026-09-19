// Wms.Domain.Tests/ValueObjects/BarcodeTests.cs

using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.ValueObjects;

public class BarcodeTests
{
    [Theory]
    [InlineData("123456789012")]
    [InlineData("234567890123")]
    [InlineData("UPC123456789")]
    [InlineData("CODE128TEST")]
    public void Constructor_WithValidValue_CreatesBarcodeSuccessfully(string validValue)
    {
        // Act
        var barcode = new Barcode(validValue);

        // Assert
        barcode.Value.Should().Be(validValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Constructor_WithInvalidValue_ThrowsArgumentException(string? invalidValue)
    {
        // Act & Assert
        var act = () => new Barcode(invalidValue!);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be empty*");
    }

    [Theory]
    [InlineData("1")] // Too short
    [InlineData("12")] // Too short
    public void Constructor_WithTooShortValue_ThrowsArgumentException(string shortValue)
    {
        // Act & Assert
        var act = () => new Barcode(shortValue);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithTooLongValue_ThrowsArgumentException()
    {
        // Arrange
        var longValue = new string('1', 101); // 101 characters

        // Act & Assert
        var act = () => new Barcode(longValue);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed 50 characters*");
    }

    [Fact]
    public void Equals_WithSameValue_ReturnsTrue()
    {
        // Arrange
        var barcode1 = new Barcode("123456789012");
        var barcode2 = new Barcode("123456789012");

        // Act & Assert
        barcode1.Should().Be(barcode2);
        (barcode1 == barcode2).Should().BeTrue();
        (barcode1 != barcode2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WithDifferentValue_ReturnsFalse()
    {
        // Arrange
        var barcode1 = new Barcode("123456789012");
        var barcode2 = new Barcode("234567890123");

        // Act & Assert
        barcode1.Should().NotBe(barcode2);
        (barcode1 == barcode2).Should().BeFalse();
        (barcode1 != barcode2).Should().BeTrue();
    }

    [Fact]
    public void GetHashCode_WithSameValue_ReturnsSameHashCode()
    {
        // Arrange
        var barcode1 = new Barcode("123456789012");
        var barcode2 = new Barcode("123456789012");

        // Act & Assert
        barcode1.GetHashCode().Should().Be(barcode2.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        // Arrange
        var value = "123456789012";
        var barcode = new Barcode(value);

        // Act
        var result = barcode.ToString();

        // Assert
        result.Should().Be(value);
    }

    [Fact]
    public void ImplicitConversion_FromString_CreatesBarcodeSuccessfully()
    {
        // Arrange
        var value = "123456789012";

        // Act
        Barcode barcode = value;

        // Assert
        barcode.Value.Should().Be(value);
    }

    [Fact]
    public void ImplicitConversion_ToString_ReturnsValue()
    {
        // Arrange
        var barcode = new Barcode("123456789012");

        // Act
        string value = barcode;

        // Assert
        value.Should().Be("123456789012");
    }
}
