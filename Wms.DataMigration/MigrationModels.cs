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
    public DataMigrationSummary Summary => new(
        new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Items"] = Items.Count,
            ["ItemBarcodes"] = Barcodes.Count,
            ["Warehouses"] = Warehouses.Count,
            ["Locations"] = Locations.Count,
            ["Lots"] = Lots.Count,
            ["Stock"] = Stock.Count,
            ["Movements"] = Movements.Count
        },
        Stock.Sum(row => row.QuantityAvailable),
        Stock.Sum(row => row.QuantityReserved),
        Movements.Sum(row => row.Quantity));
}
