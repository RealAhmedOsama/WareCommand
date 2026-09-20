using System.Globalization;

namespace Wms.DataMigration;

public sealed record DataMigrationSummary(
    IReadOnlyDictionary<string, long> RowCounts,
    decimal StockQuantityAvailable,
    decimal StockQuantityReserved,
    decimal MovementQuantity)
{
    public static DataMigrationSummary Empty { get; } = new(
        new Dictionary<string, long>(StringComparer.Ordinal),
        0,
        0,
        0);

    public bool HasRows => RowCounts.Values.Any(count => count > 0);
}

public sealed record DataMigrationReport(
    string Status,
    bool DryRun,
    string SourcePath,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string? BackupPath,
    DataMigrationSummary Source,
    DataMigrationSummary TargetBefore,
    DataMigrationSummary TargetAfter,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool Succeeded => Status is "DryRun" or "Applied";
}

internal sealed record ItemRow(
    int Id,
    string Sku,
    string Name,
    string Description,
    string UnitOfMeasure,
    bool IsActive,
    bool RequiresLot,
    bool RequiresSerial,
    int ShelfLifeDays,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

internal sealed record BarcodeRow(int Id, string Barcode, int ItemId);

internal sealed record WarehouseRow(
    int Id,
    string Code,
    string Name,
    string Address,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

internal sealed record LocationRow(
    int Id,
    string Code,
    string Name,
    int WarehouseId,
    int? ParentLocationId,
    bool IsPickable,
    bool IsReceivable,
    bool IsActive,
    int Capacity,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

internal sealed record LotRow(
    int Id,
    string Number,
    int ItemId,
    DateTime? ExpiryDate,
    DateTime? ManufacturedDate,
    DateTime? RetestDate,
    DateTime? HoldUntil,
    string? SupplierLotNumber,
    string? Notes,
    int Status,
    bool IsActive,
    string? RecallReason,
    DateTime? RecalledAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

internal sealed record StockRow(
    int Id,
    int ItemId,
    int LocationId,
    int? LotId,
    string? SerialNumber,
    decimal QuantityAvailable,
    decimal QuantityReserved,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

internal sealed record MovementRow(
    int Id,
    int Type,
    int ItemId,
    int? FromLocationId,
    int? ToLocationId,
    int? LotId,
    string? SerialNumber,
    decimal Quantity,
    string UserId,
    string? ReferenceNumber,
    string? Notes,
    DateTime Timestamp,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

internal sealed record LegacySerialRow(
    int ItemId,
    string Number,
    int? LotId,
    int? CurrentWarehouseId,
    int? CurrentLocationId,
    int Status,
    string? StatusReason,
    bool HasMigrationConflict,
    string? ConflictReason,
    DateTime? LastMovedAt,
    DateTime CreatedAt);

internal sealed record LegacySerialConflictRow(
    int ItemId,
    string Number,
    string SourceType,
    int? SourceId,
    string Reason,
    DateTime CreatedAt);

internal sealed record SourceSnapshot(
    IReadOnlyList<ItemRow> Items,
    IReadOnlyList<BarcodeRow> Barcodes,
    IReadOnlyList<WarehouseRow> Warehouses,
    IReadOnlyList<LocationRow> Locations,
    IReadOnlyList<LotRow> Lots,
    IReadOnlyList<StockRow> Stock,
    IReadOnlyList<MovementRow> Movements,
    IReadOnlyList<string> ValidationErrors)
{
    public IReadOnlyList<LegacySerialRow> LegacySerialNumbers =>
        LegacySerialMigration.BuildSerials(this);

    public IReadOnlyList<LegacySerialConflictRow> LegacySerialConflicts =>
        LegacySerialMigration.BuildConflicts(this);

    public DataMigrationSummary Summary => new(
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Items"] = Items.Count,
            ["ItemBarcodes"] = Barcodes.Count,
            ["Warehouses"] = Warehouses.Count,
            ["Locations"] = Locations.Count,
            ["Lots"] = Lots.Count,
            ["Stock"] = Stock.Count,
            ["Movements"] = Movements.Count,
            ["SerialNumbers"] = LegacySerialNumbers.Count,
            ["SerialNumberMigrationConflicts"] = LegacySerialConflicts.Count
        },
        Stock.Sum(row => row.QuantityAvailable),
        Stock.Sum(row => row.QuantityReserved),
        Movements.Sum(row => row.Quantity));
}

internal static class LegacySerialMigration
{
    private const string ConflictReason = "Legacy serial data contains conflicting lot, location, or quantity dimensions.";

    public static IReadOnlyList<LegacySerialRow> BuildSerials(SourceSnapshot source)
    {
        var locationWarehouses = source.Locations
            .ToDictionary(location => location.Id, location => location.WarehouseId);
        var inputs = BuildInputs(source);
        return inputs
            .GroupBy(input => (input.ItemId, input.Number), EqualityComparer<(int, string)>.Default)
            .Select(group =>
            {
                var rows = group.ToArray();
                var locations = rows.Select(row => row.LocationId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
                var lots = rows.Select(row => row.LotId?.ToString(CultureInfo.InvariantCulture) ?? "NULL").Distinct(StringComparer.Ordinal).ToArray();
                var totalAvailable = rows.Sum(row => row.QuantityAvailable);
                var totalReserved = rows.Sum(row => row.QuantityReserved);
                var hasConflict = rows.Any(row => row.WasTruncated) ||
                    locations.Length > 1 ||
                    lots.Length > 1 ||
                    totalAvailable is < 0m or > 1m ||
                    totalReserved is < 0m or > 1m;
                int? locationId = locations.Length == 1 ? locations[0] : null;
                var reason = hasConflict
                    ? ConflictReason
                    : null;
                int? currentWarehouseId = null;
                if (locationId.HasValue && locationWarehouses.TryGetValue(locationId.Value, out var warehouseId))
                {
                    currentWarehouseId = warehouseId;
                }

                return new LegacySerialRow(
                    group.Key.Item1,
                    group.Key.Item2,
                    rows.Select(row => row.LotId).FirstOrDefault(value => value.HasValue),
                    currentWarehouseId,
                    locationId,
                    hasConflict ? 8 : 1,
                    hasConflict ? "migration conflict" : null,
                    hasConflict,
                    reason,
                    rows.Select(row => row.CreatedAt).OrderByDescending(value => value).FirstOrDefault(),
                    rows.Select(row => row.CreatedAt).Min());
            })
            .OrderBy(row => row.ItemId)
            .ThenBy(row => row.Number)
            .ToArray();
    }

    public static IReadOnlyList<LegacySerialConflictRow> BuildConflicts(SourceSnapshot source)
    {
        var serials = BuildSerials(source)
            .Where(row => row.HasMigrationConflict)
            .Select(row => (row.ItemId, row.Number, row.ConflictReason ?? "migration conflict"))
            .ToHashSet();
        return BuildInputs(source)
            .Where(input => serials.Contains((input.ItemId, input.Number, ConflictReason)))
            .Select(input => new LegacySerialConflictRow(
                input.ItemId,
                input.Number,
                input.SourceType,
                input.SourceId,
                "Legacy serial data conflicts with one-unit identity rules.",
                input.CreatedAt))
            .ToArray();
    }

    private static List<LegacySerialInput> BuildInputs(SourceSnapshot source)
    {
        var inputs = new List<LegacySerialInput>();
        foreach (var row in source.Stock.Where(row => !string.IsNullOrWhiteSpace(row.SerialNumber)))
        {
            var number = Normalize(row.SerialNumber!, out var truncated);
            inputs.Add(new LegacySerialInput(
                row.ItemId,
                number,
                row.LotId,
                row.LocationId,
                row.QuantityAvailable,
                row.QuantityReserved,
                row.CreatedAt,
                "Stock",
                row.Id,
                truncated));
        }

        foreach (var row in source.Movements.Where(row => !string.IsNullOrWhiteSpace(row.SerialNumber)))
        {
            var number = Normalize(row.SerialNumber!, out var truncated);
            inputs.Add(new LegacySerialInput(
                row.ItemId,
                number,
                row.LotId,
                row.ToLocationId ?? row.FromLocationId,
                0m,
                0m,
                row.CreatedAt,
                "Movement",
                row.Id,
                truncated));
        }

        return inputs;
    }

    private static string Normalize(string value, out bool truncated)
    {
        var normalized = value.Trim().ToUpperInvariant();
        truncated = normalized.Length > 100;
        return truncated ? normalized[..100] : normalized;
    }

    private sealed record LegacySerialInput(
        int ItemId,
        string Number,
        int? LotId,
        int? LocationId,
        decimal QuantityAvailable,
        decimal QuantityReserved,
        DateTime CreatedAt,
        string SourceType,
        int SourceId,
        bool WasTruncated)
    {
    }
}
