using System.Collections.Concurrent;
using Wms.Application.Settings;

namespace Wms.Infrastructure.Settings;

public sealed class WmsSettingsCache
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(int? warehouseId, out WmsSettingsSnapshot snapshot)
    {
        var key = GetKey(warehouseId);
        if (_entries.TryGetValue(key, out var entry) &&
            DateTimeOffset.UtcNow - entry.CachedAtUtc <= CacheLifetime)
        {
            snapshot = entry.Snapshot;
            return true;
        }

        _entries.TryRemove(key, out _);
        snapshot = null!;
        return false;
    }

    public void Set(WmsSettingsSnapshot snapshot)
    {
        _entries[GetKey(snapshot.WarehouseId)] = new CacheEntry(
            snapshot,
            DateTimeOffset.UtcNow);
    }

    public void InvalidateAll() => _entries.Clear();

    public void Invalidate(int warehouseId) => _entries.TryRemove(GetKey(warehouseId), out _);

    private static string GetKey(int? warehouseId) =>
        warehouseId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "global";

    private sealed record CacheEntry(WmsSettingsSnapshot Snapshot, DateTimeOffset CachedAtUtc);
}
