using FluentAssertions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;

namespace Wms.Domain.Tests.Entities;

public sealed class InventoryTransactionTests
{
    [Fact]
    public void ConstructorRequiresBalancedBeforeAfterEquation()
    {
        var act = () => new InventoryTransaction(
            InventoryTransactionType.Receipt,
            CreateKey(),
            quantityDelta: 5m,
            quantityBefore: 0m,
            quantityAfter: 4m,
            reservedQuantityDelta: 0m,
            reservedQuantityBefore: 0m,
            reservedQuantityAfter: 0m,
            actorUserId: "user-1",
            occurredAtUtc: DateTime.UtcNow,
            correlationId: "correlation-1",
            idempotencyKey: "idempotency-1",
            transactionGroupId: "group-1",
            entrySequence: 1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*before quantity plus delta*");
    }

    [Fact]
    public void ConstructorPreservesSignedDeltaAndAuditDimensions()
    {
        var transaction = new InventoryTransaction(
            InventoryTransactionType.Transfer,
            CreateKey(),
            quantityDelta: -3m,
            quantityBefore: 10m,
            quantityAfter: 7m,
            reservedQuantityDelta: 0m,
            reservedQuantityBefore: 2m,
            reservedQuantityAfter: 2m,
            actorUserId: "user-1",
            occurredAtUtc: DateTime.SpecifyKind(new DateTime(2026, 9, 20, 10, 0, 0), DateTimeKind.Utc),
            correlationId: "correlation-1",
            idempotencyKey: "idempotency-1",
            transactionGroupId: "group-1",
            entrySequence: 1,
            referenceType: "Movement",
            referenceId: "MOVE-1",
            reason: "relocation");

        transaction.Type.Should().Be(InventoryTransactionType.Transfer);
        transaction.QuantityDelta.Should().Be(-3m);
        transaction.QuantityBefore.Should().Be(10m);
        transaction.QuantityAfter.Should().Be(7m);
        transaction.ReferenceType.Should().Be("Movement");
        transaction.ReferenceId.Should().Be("MOVE-1");
    }

    private static InventoryBalanceKey CreateKey() => new(
        warehouseId: 1,
        locationId: 2,
        itemId: 3,
        lotId: 4,
        serialNumberId: 5,
        serialNumber: "SN-5",
        licensePlateId: 6,
        inventoryStatusId: 1,
        baseUnitOfMeasure: "ea");
}
