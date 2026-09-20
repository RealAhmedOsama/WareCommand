using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.Entities;

public sealed class LicensePlateTests
{
    [Fact]
    public void SsccIsNormalizedAndCheckDigitValidated()
    {
        var plate = new LicensePlate(
            " 000123456789012343 ",
            LicensePlateType.Pallet,
            warehouseId: 7,
            isSscc: true);

        plate.Number.Should().Be("000123456789012343");
        plate.IsSscc.Should().BeTrue();
        plate.Status.Should().Be(LicensePlateStatus.Open);
    }

    [Fact]
    public void InvalidSsccIsRejected()
    {
        var act = () => new LicensePlate(
            "000123456789012344",
            LicensePlateType.Pallet,
            warehouseId: 7,
            isSscc: true);

        act.Should().Throw<ArgumentException>().WithMessage("*check digit*");
    }

    [Fact]
    public void SerialContentMustRemainOneUnit()
    {
        var act = () => new LicensePlateContent(
            licensePlateId: 1,
            itemId: 2,
            new Quantity(2),
            serialNumberId: 3);

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one*");
    }

    [Fact]
    public void ShippedPlateCannotBeMutatedOrReturnedToAnotherWarehouse()
    {
        var plate = new LicensePlate("LPN-1", LicensePlateType.Tote, warehouseId: 7, currentLocationId: 11);
        plate.Close();
        plate.Ship();

        var move = () => plate.MoveTo(7, 12);
        var wrongWarehouseReturn = () => plate.ReturnTo(8, 12);

        move.Should().Throw<InvalidOperationException>();
        wrongWarehouseReturn.Should().Throw<InvalidOperationException>();
        plate.Status.Should().Be(LicensePlateStatus.Shipped);
        plate.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ParentCannotBeItself()
    {
        var plate = new LicensePlate("LPN-1", LicensePlateType.Carton, warehouseId: 7);
        typeof(LicensePlate)
            .GetProperty(nameof(LicensePlate.Id))!
            .SetValue(plate, 1);

        var act = () => plate.SetParent(1);

        act.Should().Throw<InvalidOperationException>().WithMessage("*contain itself*");
    }
}
