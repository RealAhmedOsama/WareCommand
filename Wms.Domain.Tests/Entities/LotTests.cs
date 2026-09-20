using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class LotTests
{
    [Fact]
    public void ExpiryDecisionsUseTheSuppliedBusinessDate()
    {
        var lot = new Lot(
            "LOT-001",
            7,
            new DateTime(2026, 9, 20, 18, 45, 0),
            new DateTime(2026, 8, 1, 12, 30, 0));

        lot.IsExpired(new DateOnly(2026, 9, 21)).Should().BeTrue();
        lot.IsExpired(new DateOnly(2026, 9, 20)).Should().BeFalse();
        lot.IsExpiringSoon(new DateOnly(2026, 8, 25), warningDays: 30).Should().BeTrue();
    }

    [Fact]
    public void LotDatesAreStoredAsDateOnlyMidnightValues()
    {
        var lot = new Lot(
            "LOT-002",
            7,
            new DateTime(2026, 9, 20, 18, 45, 0),
            new DateTime(2026, 8, 1, 12, 30, 0));

        lot.ExpiryDate.Should().Be(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Unspecified));
        lot.ManufacturedDate.Should().Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void LotIdentityAndMetadataAreNormalized()
    {
        var lot = new Lot(
            " lot-a ",
            7,
            expiryDate: new DateTime(2026, 9, 30, 10, 0, 0),
            manufacturedDate: new DateTime(2026, 8, 1, 10, 0, 0),
            retestDate: new DateTime(2026, 9, 15, 10, 0, 0),
            holdUntil: new DateTime(2026, 9, 10, 10, 0, 0),
            supplierLotNumber: " supplier-a ",
            notes: " received ");

        lot.Number.Should().Be("LOT-A");
        lot.SupplierLotNumber.Should().Be("supplier-a");
        lot.Notes.Should().Be("received");
        lot.Status.Should().Be(LotStatus.Active);
    }

    [Fact]
    public void AllocationHonorsExpiryRetestAndHoldBoundaries()
    {
        var lot = new Lot(
            "LOT-A",
            7,
            expiryDate: new DateTime(2026, 9, 30),
            manufacturedDate: new DateTime(2026, 8, 1),
            retestDate: new DateTime(2026, 9, 20),
            holdUntil: new DateTime(2026, 9, 19));

        lot.IsAllocationEligible(new DateOnly(2026, 9, 20)).Should().BeTrue();
        lot.IsAllocationEligible(new DateOnly(2026, 9, 21)).Should().BeFalse();

        lot.SetStatus(LotStatus.Hold, "quality review");
        lot.IsAllocationEligible(new DateOnly(2026, 9, 20)).Should().BeFalse();
        lot.SetStatus(LotStatus.Released, "quality released");
        lot.IsAllocationEligible(new DateOnly(2026, 9, 20)).Should().BeTrue();
        lot.IsAllocationEligible(new DateOnly(2026, 10, 1)).Should().BeFalse();
    }

    [Fact]
    public void RecalledLotCanOnlyBeClosedAndRequiresReason()
    {
        var lot = new Lot("LOT-A", 7);

        var act = () => lot.SetStatus(LotStatus.Recalled, "");
        act.Should().Throw<ArgumentException>();

        lot.SetStatus(LotStatus.Recalled, "supplier recall");
        lot.Status.Should().Be(LotStatus.Recalled);
        lot.RecallReason.Should().Be("supplier recall");
        lot.IsReceivingAllowed(new DateOnly(2026, 9, 20), blockExpired: false).Should().BeFalse();

        lot.SetStatus(LotStatus.Closed, "case closed");
        lot.Status.Should().Be(LotStatus.Closed);
        lot.IsActive.Should().BeFalse();
    }
}
