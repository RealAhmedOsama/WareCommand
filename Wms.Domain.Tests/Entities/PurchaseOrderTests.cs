using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class PurchaseOrderTests
{
    [Fact]
    public void DraftCanConfirmButCannotBeEditedOrCancelledAfterConfirmation()
    {
        var order = CreateOrder();
        order.Status.Should().Be(PurchaseOrderStatus.Draft);

        order.Confirm("user-1", DateTime.UtcNow);

        order.Status.Should().Be(PurchaseOrderStatus.Confirmed);
        order.CanEdit.Should().BeFalse();
        var edit = () => order.UpdateDraft(
            new DateOnly(2026, 9, 21),
            null,
            null,
            "MANUAL",
            null,
            null,
            null);
        edit.Should().Throw<InvalidOperationException>();
        var cancel = () => order.Cancel("user-1", DateTime.UtcNow);
        cancel.Should().NotThrow();
        order.Status.Should().Be(PurchaseOrderStatus.Cancelled);
    }

    [Fact]
    public void LineRejectsOverDeliveryAndClosesAtUnderDeliveryTolerance()
    {
        var line = CreateLine(overTolerance: 10m, underTolerance: 20m);

        line.RecordReceipt(8m);
        line.ReceivedBaseQuantity.Should().Be(8m);
        line.Close();
        line.IsClosed.Should().BeTrue();

        var over = () => line.RecordReceipt(3m);
        over.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClosedOrderCanReopenAndRecalculateReceiptStatus()
    {
        var order = CreateOrder();
        var line = order.Lines.Single();
        order.Confirm("user-1", DateTime.UtcNow);
        line.RecordReceipt(10m);
        order.Close("user-1", DateTime.UtcNow);

        order.Status.Should().Be(PurchaseOrderStatus.Closed);
        order.Reopen();

        order.Status.Should().Be(PurchaseOrderStatus.Received);
        line.IsClosed.Should().BeFalse();
    }

    private static PurchaseOrder CreateOrder()
    {
        var order = new PurchaseOrder(
            "PO-TEST-000001",
            1,
            "TEST",
            1,
            "SUP-1",
            "Supplier",
            new DateOnly(2026, 9, 20));
        order.ReplaceDraftLines([CreateLine()]);
        return order;
    }

    private static PurchaseOrderLine CreateLine(
        decimal overTolerance = 0m,
        decimal underTolerance = 0m) => new(
        1,
        1,
        "ITEM-1",
        "Item",
        "EA",
        10m,
        "EA",
        10m,
        1m,
        0,
        QuantityRoundingMode.Reject,
        0m,
        "EA -> EA",
        string.Empty,
        overTolerance,
        underTolerance);
}
