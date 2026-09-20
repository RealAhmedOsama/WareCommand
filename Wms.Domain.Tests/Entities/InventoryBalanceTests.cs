using FluentAssertions;
using Wms.Domain.Entities;
using Wms.Domain.Inventory;

namespace Wms.Domain.Tests.Entities;

public sealed class InventoryBalanceTests
{
    [Fact]
    public void ApplyTracksOnHandReservationsAndAvailability()
    {
        var balance = new InventoryBalance(CreateKey());

        balance.Apply(10m, 3m, allowNegativeStock: false);

        balance.OnHandQuantity.Should().Be(10m);
        balance.ReservedQuantity.Should().Be(3m);
        balance.AvailableQuantity.Should().Be(7m);
        balance.Revision.Should().Be(1);
    }

    [Fact]
    public void ApplyRejectsNegativeOnHandUnlessWarehousePolicyAllowsIt()
    {
        var balance = new InventoryBalance(CreateKey());

        var act = () => balance.Apply(-1m, 0m, allowNegativeStock: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot become negative*");
        balance.OnHandQuantity.Should().Be(0m);
    }

    [Fact]
    public void ApplyRejectsReservationsAbovePhysicalOnHand()
    {
        var balance = new InventoryBalance(CreateKey());

        var act = () => balance.Apply(2m, 3m, allowNegativeStock: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot exceed physical on-hand*");
    }

    private static InventoryBalanceKey CreateKey() => new(
        warehouseId: 1,
        locationId: 2,
        itemId: 3,
        lotId: null,
        serialNumberId: null,
        serialNumber: null,
        licensePlateId: null,
        inventoryStatusId: 1,
        baseUnitOfMeasure: "ea");
}
