using Wms.Domain.Entities;

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
}
