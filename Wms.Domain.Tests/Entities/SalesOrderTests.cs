using FluentAssertions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.Entities;

public sealed class SalesOrderTests
{
    [Fact]
    public void ConfirmHoldAndReleasePreserveTheControlledStateMachine()
    {
        var order = CreateOrder();
        order.ReplaceDraftLines([CreateLine()]);
        order.Confirm("user-1", DateTime.UtcNow);
        order.Hold("credit review", "user-1", DateTime.UtcNow);
        order.ReleaseHold();

        order.Status.Should().Be(SalesOrderStatus.Confirmed);
        order.HoldReason.Should().BeNull();
        order.ConfirmedByUserId.Should().Be("user-1");
    }

    [Fact]
    public void QuantityLedgerRejectsOverAllocationAndKeepsBackorderMath()
    {
        var line = CreateLine();

        line.RecordAllocation(6m);
        line.RecordPicked(4m);
        line.RecordPacked(4m);
        line.RecordShipped(2m);
        line.CancelQuantity(4m);

        line.AllocatedBaseQuantity.Should().Be(6m);
        line.BackorderBaseQuantity.Should().Be(0m);
        line.RemainingToShipBaseQuantity.Should().Be(2m);
        var overAllocation = () => line.RecordAllocation(1m);
        overAllocation.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConfirmedOrderCannotRefreshCustomerSnapshot()
    {
        var order = CreateOrder();
        order.ReplaceDraftLines([CreateLine()]);
        order.Confirm("user-1", DateTime.UtcNow);

        var refresh = () => order.RefreshCustomerSnapshot(new CustomerOrderSnapshot(
            1,
            "NEW-CUSTOMER",
            "New Customer",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            100,
            null,
            null,
            false));

        refresh.Should().Throw<InvalidOperationException>();
    }

    private static SalesOrder CreateOrder() =>
        new(
            "SO-WH-000001",
            1,
            "WH",
            1,
            "CUST",
            "Customer",
            "عميل",
            "Contact",
            "contact@example.com",
            "+201000000000",
            1,
            "MAIN",
            "Recipient",
            "+201100000000",
            "EG",
            "Cairo",
            "Cairo",
            "11511",
            "First line",
            null,
            null,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 22),
            "EXT-1",
            "MANUAL",
            null,
            100,
            "CARRIER",
            "STANDARD",
            "BOX",
            "LABEL",
            true,
            null,
            "user-1");

    private static SalesOrderLine CreateLine() =>
        new(
            1,
            1,
            "ITEM-1",
            "Item one",
            "الصنف",
            "CUST-ITEM-1",
            "EA",
            10m,
            "EA",
            10m,
            1m,
            0,
            "EA -> EA",
            string.Empty);
}
