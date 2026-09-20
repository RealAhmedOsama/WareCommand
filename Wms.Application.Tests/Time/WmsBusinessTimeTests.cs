using Wms.Application.Context;
using Wms.Application.Time;

namespace Wms.Application.Tests.Time;

public sealed class WmsBusinessTimeTests
{
    [Fact]
    public void FakeClockProducesDeterministicWarehouseBusinessDate()
    {
        IClock clock = new FixedClock(
            new DateTimeOffset(2026, 9, 20, 21, 30, 0, TimeSpan.Zero));

        WmsBusinessTime.GetBusinessDate(clock.UtcNow, "Africa/Cairo")
            .Should()
            .Be(new DateOnly(2026, 9, 21));
    }

    [Fact]
    public void NewYorkDstSpringForwardProducesA23HourBusinessDay()
    {
        var beforeTransition = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
        var afterTransition = new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero);

        WmsBusinessTime.ToLocal(beforeTransition, "America/New_York")
            .DateTime
            .Should()
            .Be(new DateTime(2026, 3, 8, 1, 30, 0));
        WmsBusinessTime.ToLocal(afterTransition, "America/New_York")
            .DateTime
            .Should()
            .Be(new DateTime(2026, 3, 8, 3, 30, 0));

        var range = WmsBusinessTime.GetInclusiveDateRange(
            new DateOnly(2026, 3, 8),
            new DateOnly(2026, 3, 8),
            "America/New_York");

        range.FromUtc.Should().Be(new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc));
        range.ToUtcExclusive.Should().Be(new DateTime(2026, 3, 9, 4, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TimeZoneCatalogNormalizesLegacyWindowsIdsToIanaIds()
    {
        WmsTimeZoneCatalog.TryNormalize("Eastern Standard Time", out var normalized)
            .Should()
            .BeTrue();

        normalized.Should().Be("America/New_York");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
