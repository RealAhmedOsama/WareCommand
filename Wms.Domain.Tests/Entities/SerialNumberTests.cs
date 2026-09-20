using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class SerialNumberTests
{
    [Fact]
    public void SerialIdentityIsNormalizedAndReceiptStoresCurrentLocation()
    {
        var serial = new SerialNumber(" sn-001 ", 7, lotId: 11);

        serial.Number.Should().Be("SN-001");
        serial.ItemId.Should().Be(7);
        serial.LotId.Should().Be(11);

        serial.RecordReceipt(
            warehouseId: 3,
            locationId: 8,
            lotId: 11,
            referenceNumber: "PO-1",
            quarantine: false,
            timestampUtc: new DateTime(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc));

        serial.Status.Should().Be(SerialStatus.Available);
        serial.CurrentWarehouseId.Should().Be(3);
        serial.CurrentLocationId.Should().Be(8);
        serial.ReceiptReference.Should().Be("PO-1");
        serial.IsAllocationEligible.Should().BeTrue();
    }

    [Fact]
    public void SerialCannotBeMovedAfterScrapOrShipment()
    {
        var serial = new SerialNumber("SN-002", 7);
        serial.RecordReceipt(3, 8, null, "PO-2", false, DateTime.UtcNow);
        serial.SetStatus(SerialStatus.Shipped, "shipment completed", DateTime.UtcNow);

        var act = () => serial.MoveTo(3, 9, null, DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();

        serial.SetStatus(SerialStatus.Returned, "customer return", DateTime.UtcNow);
        serial.RecordReceipt(3, 9, null, "RMA-1", false, DateTime.UtcNow);
        serial.Status.Should().Be(SerialStatus.Available);
        serial.CurrentLocationId.Should().Be(9);
    }

    [Fact]
    public void SerialStatusChangesRequireReasonAndBlockScrapRecovery()
    {
        var serial = new SerialNumber("SN-003", 7);
        serial.RecordReceipt(3, 8, null, null, false, DateTime.UtcNow);

        var missingReason = () => serial.SetStatus(SerialStatus.Hold, "");
        missingReason.Should().Throw<ArgumentException>();

        serial.SetStatus(SerialStatus.Scrapped, "irreparable damage", DateTime.UtcNow);
        serial.CanTransitionTo(SerialStatus.Available).Should().BeFalse();
        var recovery = () => serial.SetStatus(SerialStatus.Available, "recovered", DateTime.UtcNow);
        recovery.Should().Throw<InvalidOperationException>();
    }
}
