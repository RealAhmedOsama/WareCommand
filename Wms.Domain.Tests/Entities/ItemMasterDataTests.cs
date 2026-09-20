using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class ItemMasterDataTests
{
    [Fact]
    public void UpdateMasterData_StoresCommercialControlsAndComputesVolume()
    {
        var item = new Item("MASTER-001", "Widget", "EA");

        item.UpdateMasterData(new ItemMasterDetails(
            Name: "Widget",
            LocalizedName: "أداة",
            Category: "Hardware",
            Brand: "Acme",
            Type: ItemType.FinishedGood,
            LifecycleStatus: ItemLifecycleStatus.Draft,
            PurchaseUnit: "BOX",
            SalesUnit: "EA",
            LengthCm: 10,
            WidthCm: 20,
            HeightCm: 5,
            RequiresExpiry: true,
            ShelfLifeDays: 30,
            UseFefo: true,
            StandardCost: 12.5m,
            SalesPrice: 20m,
            ReorderPolicy: ItemReorderPolicy.MinMax,
            MinimumStock: 5,
            MaximumStock: 50,
            SafetyStock: 10,
            LeadTimeDays: 7));

        item.Sku.Should().Be("MASTER-001");
        item.Type.Should().Be(ItemType.FinishedGood);
        item.LifecycleStatus.Should().Be(ItemLifecycleStatus.Draft);
        item.IsActive.Should().BeFalse();
        item.PurchaseUnit.Should().Be("BOX");
        item.VolumeCubicMeters.Should().Be(0.001m);
        item.UseFefo.Should().BeTrue();
        item.MinimumStock.Should().Be(5);
    }

    [Fact]
    public void UpdateMasterData_RejectsIncompatibleExpiryAndFefoPolicies()
    {
        var item = new Item("MASTER-002", "Widget", "EA");

        var expiryWithoutShelfLife = () => item.UpdateMasterData(new ItemMasterDetails(
            Name: "Widget",
            RequiresExpiry: true));
        var fefoWithoutExpiry = () => item.UpdateMasterData(new ItemMasterDetails(
            Name: "Widget",
            UseFefo: true));

        expiryWithoutShelfLife.Should().Throw<ArgumentException>().WithMessage("*shelf life*");
        fefoWithoutExpiry.Should().Throw<ArgumentException>().WithMessage("*FEFO*");
    }

    [Fact]
    public void UpdateMasterData_RejectsInvalidRanges()
    {
        var item = new Item("MASTER-003", "Widget", "EA");

        var stockRange = () => item.UpdateMasterData(new ItemMasterDetails(
            Name: "Widget",
            MinimumStock: 10,
            MaximumStock: 5));
        var temperatureRange = () => item.UpdateMasterData(new ItemMasterDetails(
            Name: "Widget",
            MinimumTemperatureCelsius: 10,
            MaximumTemperatureCelsius: 5));

        stockRange.Should().Throw<ArgumentException>().WithMessage("*Minimum stock*");
        temperatureRange.Should().Throw<ArgumentException>().WithMessage("*Minimum temperature*");
    }

    [Fact]
    public void AddPackaging_RejectsDuplicateCodesAndAcceptsDefaultDefinition()
    {
        var item = new Item("MASTER-004", "Widget", "EA");
        var packaging = new ItemPackaging("CASE", "EA", 12, "123456");
        item.AddPackaging(packaging);

        var duplicate = () => item.AddPackaging(new ItemPackaging("case", "EA", 24));

        item.Packagings.Should().ContainSingle().Which.Barcode.Should().Be("123456");
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public void ItemPackaging_RejectsShortBarcodeAndNegativeDimensions()
    {
        var shortBarcode = () => new ItemPackaging("CASE", "EA", 12, "12");
        var negativeDimension = () => new ItemPackaging("CASE", "EA", 12, lengthCm: -1);

        shortBarcode.Should().Throw<ArgumentException>().WithMessage("*at least 3*");
        negativeDimension.Should().Throw<ArgumentOutOfRangeException>();
    }
}
