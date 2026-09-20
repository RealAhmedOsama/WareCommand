using Wms.Application.Settings;
using Wms.Infrastructure.Settings;

namespace Wms.Infrastructure.Tests.Settings;

public sealed class WmsSettingsCacheTests
{
    [Fact]
    public void GlobalAndWarehouseEntriesCanBeInvalidatedIndependentlyOrTogether()
    {
        var cache = new WmsSettingsCache();
        var global = new WmsSettingsSnapshot(
            null,
            "global",
            DateTimeOffset.UtcNow,
            WmsSettingsDefaults.Create());
        var warehouse = new WmsSettingsSnapshot(
            7,
            "warehouse:7",
            DateTimeOffset.UtcNow,
            WmsSettingsPrecedence.Apply(
                WmsSettingsDefaults.Create(),
                new WmsWarehouseSettingsOverrides { LowStockThreshold = 2 }));

        cache.Set(global);
        cache.Set(warehouse);
        cache.TryGet(null, out _).Should().BeTrue();
        cache.TryGet(7, out _).Should().BeTrue();

        cache.Invalidate(7);

        cache.TryGet(null, out _).Should().BeTrue();
        cache.TryGet(7, out _).Should().BeFalse();

        cache.InvalidateAll();
        cache.TryGet(null, out _).Should().BeFalse();
    }
}
