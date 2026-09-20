using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;

namespace Wms.Domain.Tests.Entities;

public sealed class ReceiptTests
{
    [Fact]
    public void PartialPhysicalReceiptCompletesAsPartiallyCompletedAndCannotCompleteTwice()
    {
        var receipt = CreateReceipt();
        var line = CreateLine(expectedBaseQuantity: 10m);
        receipt.AddLine(line);

        receipt.Open("receiver-1", DateTime.UtcNow);
        receipt.StartReceiving("receiver-1", DateTime.UtcNow);
        line.RecordPhysicalReceipt(8m, 6m, 1m, 1m, 0m);
        receipt.Complete("receiver-1", DateTime.UtcNow);

        receipt.Status.Should().Be(ReceiptStatus.PartiallyCompleted);
        line.ReceivedBaseQuantity.Should().Be(8m);
        line.RemainingBaseQuantity.Should().Be(2m);

        var completeAgain = () => receipt.Complete("receiver-1", DateTime.UtcNow);
        completeAgain.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PhysicalReceiptRequiresBalancedAcceptedAndExceptionQuantities()
    {
        var line = CreateLine(expectedBaseQuantity: 10m);

        var unbalanced = () => line.RecordPhysicalReceipt(5m, 4m, 0m, 0m, 0m);

        unbalanced.Should().Throw<InvalidOperationException>();
        line.ReceivedBaseQuantity.Should().Be(0m);
    }

    [Fact]
    public void ReceiptMovementPreservesComponentQuantitiesAndRelatedMovement()
    {
        var receipt = CreateReceipt();
        var line = CreateLine(expectedBaseQuantity: 5m);
        receipt.AddLine(line);
        var movement = Movement.CreateReceipt(10, 2, new Quantity(5m), "receiver-1");
        movement.LinkReceipt(1, 1, ReceiptMovementKind.Receipt);

        var history = new ReceiptLineMovement(
            line,
            movement,
            5m,
            ReceiptMovementKind.Receipt,
            "receiver-1",
            DateTime.UtcNow,
            acceptedBaseQuantity: 4m,
            rejectedBaseQuantity: 1m);

        history.AcceptedBaseQuantity.Should().Be(4m);
        history.RejectedBaseQuantity.Should().Be(1m);
        history.Kind.Should().Be(ReceiptMovementKind.Receipt);
    }

    private static Receipt CreateReceipt() => new(
        "RCPT-WH-000001",
        1,
        "WH",
        null,
        null,
        null,
        null,
        null,
        null,
        2,
        createdByUserId: "planner-1");

    private static ReceiptLine CreateLine(decimal expectedBaseQuantity) => new(
        1,
        1,
        10,
        "ITEM-1",
        "Widget",
        "EA",
        expectedBaseQuantity,
        "EA",
        expectedBaseQuantity,
        1m,
        0,
        QuantityRoundingMode.Reject,
        0m,
        "EA -> EA",
        string.Empty,
        receivingLocationId: 2);
}
