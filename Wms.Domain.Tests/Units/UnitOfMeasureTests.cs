using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.Units;

public sealed class UnitOfMeasureTests
{
    [Fact]
    public void UnitOfMeasure_NormalizesCodeAndPreservesLocalizedMetadata()
    {
        var unit = new UnitOfMeasure(
            " ea ",
            UnitOfMeasureCategory.Count,
            0,
            " ea ",
            " Each ",
            " قطعة ");

        unit.Code.Should().Be("EA");
        unit.Symbol.Should().Be("ea");
        unit.Name.Should().Be("Each");
        unit.LocalizedName.Should().Be("قطعة");
        unit.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void UnitOfMeasure_RejectsPrecisionOutsideExplicitRange(int precision)
    {
        var act = () => new UnitOfMeasure(
            "EA",
            UnitOfMeasureCategory.Count,
            precision,
            "ea",
            "Each");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ItemUnitConversion_RejectsNonPositiveAndSelfConversions()
    {
        var nonPositive = () => new ItemUnitConversion(
            1,
            "CASE",
            "EA",
            0m,
            0);
        var selfConversion = () => new ItemUnitConversion(
            1,
            "EA",
            "EA",
            1m,
            0);

        nonPositive.Should().Throw<ArgumentOutOfRangeException>();
        selfConversion.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Quantity_PreservesExactDecimalAndConversionSnapshot()
    {
        var snapshot = new QuantityConversionSnapshot(
            1.234567891234m,
            "PALLET",
            "EA",
            12m,
            6,
            QuantityRoundingMode.ToEven,
            0.000000001m,
            "PALLET -> CASE -> EA",
            "|10||11|");

        var quantity = new Quantity(14.814814694808m, snapshot);

        quantity.Value.Should().Be(14.814814694808m);
        quantity.ConversionSnapshot.Should().BeSameAs(snapshot);
        quantity.ConversionSnapshot!.ConversionPath.Should().Be("PALLET -> CASE -> EA");
    }
}
