using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Wms.DataMigration;

internal sealed class SqliteSourceReader(string sourcePath)
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredColumns =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Items"] = ["Id", "Sku", "Name", "Description", "UnitOfMeasure", "IsActive", "RequiresLot", "RequiresSerial", "ShelfLifeDays", "CreatedAt", "UpdatedAt"],
            ["ItemBarcodes"] = ["Id", "Barcode", "ItemId"],
            ["Warehouses"] = ["Id", "Code", "Name", "Address", "IsActive", "CreatedAt", "UpdatedAt"],
            ["Locations"] = ["Id", "Code", "Name", "WarehouseId", "ParentLocationId", "IsPickable", "IsReceivable", "IsActive", "Capacity", "CreatedAt", "UpdatedAt"],
            ["Lots"] = ["Id", "Number", "ItemId", "ExpiryDate", "ManufacturedDate", "IsActive", "CreatedAt", "UpdatedAt"],
            ["Stock"] = ["Id", "ItemId", "LocationId", "LotId", "SerialNumber", "QuantityAvailable", "QuantityReserved", "CreatedAt", "UpdatedAt"],
            ["Movements"] = ["Id", "Type", "ItemId", "FromLocationId", "ToLocationId", "LotId", "SerialNumber", "Quantity", "UserId", "ReferenceNumber", "Notes", "Timestamp", "CreatedAt", "UpdatedAt"]
        };

    public async Task<SourceSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("SQLite source database was not found.", sourcePath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(sourcePath),
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var schemaErrors = await ValidateSchemaAsync(connection, cancellationToken);
        if (schemaErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "SQLite source schema is incompatible with the WareCommand import contract: " +
                string.Join("; ", schemaErrors));
        }

        var items = await ReadItemsAsync(connection, cancellationToken);
        var barcodes = await ReadBarcodesAsync(connection, cancellationToken);
        var warehouses = await ReadWarehousesAsync(connection, cancellationToken);
        var locations = await ReadLocationsAsync(connection, cancellationToken);
        var lots = await ReadLotsAsync(connection, cancellationToken);
        var stock = await ReadStockAsync(connection, cancellationToken);
        var movements = await ReadMovementsAsync(connection, cancellationToken);

        var snapshot = new SourceSnapshot(
            items,
            barcodes,
            warehouses,
            locations,
            lots,
            stock,
            movements,
            []);

        return snapshot with { ValidationErrors = ValidateRelationships(snapshot) };
    }

    private static async Task<List<string>> ValidateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        foreach (var (table, columns) in RequiredColumns)
        {
            if (!await TableExistsAsync(connection, table, cancellationToken))
            {
                errors.Add($"missing table {table}");
                continue;
            }

            var actualColumns = await ReadColumnsAsync(connection, table, cancellationToken);
            foreach (var column in columns)
            {
                if (!actualColumns.Contains(column))
                {
                    errors.Add($"table {table} is missing column {column}");
                }
            }
        }

        return errors;
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table LIMIT 1;";
        command.Parameters.AddWithValue("$table", table);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static Task<List<ItemRow>> ReadItemsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return ReadRowsAsync(
            connection,
            "SELECT \"Id\", \"Sku\", \"Name\", \"Description\", \"UnitOfMeasure\", \"IsActive\", \"RequiresLot\", \"RequiresSerial\", \"ShelfLifeDays\", \"CreatedAt\", \"UpdatedAt\" FROM \"Items\" ORDER BY \"Id\";",
            reader => new ItemRow(
                ReadInt32(reader, "Id"),
                ReadString(reader, "Sku"),
                ReadString(reader, "Name"),
                ReadStringOrEmpty(reader, "Description"),
                ReadString(reader, "UnitOfMeasure"),
                ReadBoolean(reader, "IsActive"),
                ReadBoolean(reader, "RequiresLot"),
                ReadBoolean(reader, "RequiresSerial"),
                ReadInt32(reader, "ShelfLifeDays"),
                ReadUtc(reader, "CreatedAt"),
                ReadNullableUtc(reader, "UpdatedAt")),
            cancellationToken);
    }

    private static Task<List<BarcodeRow>> ReadBarcodesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return ReadRowsAsync(
            connection,
            "SELECT \"Id\", \"Barcode\", \"ItemId\" FROM \"ItemBarcodes\" ORDER BY \"Id\";",
            reader => new BarcodeRow(
                ReadInt32(reader, "Id"),
                ReadString(reader, "Barcode"),
                ReadInt32(reader, "ItemId")),
            cancellationToken);
    }

    private static Task<List<WarehouseRow>> ReadWarehousesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return ReadRowsAsync(
            connection,
            "SELECT \"Id\", \"Code\", \"Name\", \"Address\", \"IsActive\", \"CreatedAt\", \"UpdatedAt\" FROM \"Warehouses\" ORDER BY \"Id\";",
            reader => new WarehouseRow(
                ReadInt32(reader, "Id"),
                ReadString(reader, "Code"),
                ReadString(reader, "Name"),
                ReadStringOrEmpty(reader, "Address"),
                ReadBoolean(reader, "IsActive"),
                ReadUtc(reader, "CreatedAt"),
                ReadNullableUtc(reader, "UpdatedAt")),
            cancellationToken);
    }

    private static Task<List<LocationRow>> ReadLocationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return ReadRowsAsync(
            connection,
            "SELECT \"Id\", \"Code\", \"Name\", \"WarehouseId\", \"ParentLocationId\", \"IsPickable\", \"IsReceivable\", \"IsActive\", \"Capacity\", \"CreatedAt\", \"UpdatedAt\" FROM \"Locations\" ORDER BY \"Id\";",
            reader => new LocationRow(
                ReadInt32(reader, "Id"),
                ReadString(reader, "Code"),
                ReadString(reader, "Name"),
                ReadInt32(reader, "WarehouseId"),
                ReadNullableInt32(reader, "ParentLocationId"),
                ReadBoolean(reader, "IsPickable"),
                ReadBoolean(reader, "IsReceivable"),
                ReadBoolean(reader, "IsActive"),
                ReadInt32(reader, "Capacity"),
                ReadUtc(reader, "CreatedAt"),
                ReadNullableUtc(reader, "UpdatedAt")),
            cancellationToken);
    }

    private static async Task<List<LotRow>> ReadLotsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = await ReadColumnsAsync(connection, "Lots", cancellationToken);
        static string SelectColumn(HashSet<string> available, string column, string fallback) =>
            available.Contains(column)
                ? $"\"{column}\""
                : $"{fallback} AS \"{column}\"";

        var sql = $"""
            SELECT "Id", "Number", "ItemId", "ExpiryDate", "ManufacturedDate",
                   {SelectColumn(columns, "RetestDate", "NULL")},
                   {SelectColumn(columns, "HoldUntil", "NULL")},
                   {SelectColumn(columns, "SupplierLotNumber", "NULL")},
                   {SelectColumn(columns, "Notes", "NULL")},
                   {SelectColumn(columns, "Status", "1")},
                   "IsActive",
                   {SelectColumn(columns, "RecallReason", "NULL")},
                   {SelectColumn(columns, "RecalledAt", "NULL")},
                   "CreatedAt", "UpdatedAt"
            FROM "Lots"
            ORDER BY "Id";
            """;

        return await ReadRowsAsync(
            connection,
            sql,
            reader => new LotRow(
                ReadInt32(reader, "Id"),
                ReadString(reader, "Number"),
                ReadInt32(reader, "ItemId"),
                ReadNullableDate(reader, "ExpiryDate"),
                ReadNullableDate(reader, "ManufacturedDate"),
                ReadNullableDate(reader, "RetestDate"),
                ReadNullableDate(reader, "HoldUntil"),
                ReadNullableString(reader, "SupplierLotNumber"),
                ReadNullableString(reader, "Notes"),
                ReadInt32(reader, "Status"),
                ReadBoolean(reader, "IsActive"),
                ReadNullableString(reader, "RecallReason"),
                ReadNullableUtc(reader, "RecalledAt"),
                ReadUtc(reader, "CreatedAt"),
                ReadNullableUtc(reader, "UpdatedAt")),
            cancellationToken);
    }

    private static Task<List<StockRow>> ReadStockAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return ReadRowsAsync(
            connection,
            "SELECT \"Id\", \"ItemId\", \"LocationId\", \"LotId\", \"SerialNumber\", \"QuantityAvailable\", \"QuantityReserved\", \"CreatedAt\", \"UpdatedAt\" FROM \"Stock\" ORDER BY \"Id\";",
            reader => new StockRow(
                ReadInt32(reader, "Id"),
                ReadInt32(reader, "ItemId"),
                ReadInt32(reader, "LocationId"),
                ReadNullableInt32(reader, "LotId"),
                ReadNullableString(reader, "SerialNumber"),
                ReadDecimal(reader, "QuantityAvailable"),
                ReadDecimal(reader, "QuantityReserved"),
                ReadUtc(reader, "CreatedAt"),
                ReadNullableUtc(reader, "UpdatedAt")),
            cancellationToken);
    }

    private static Task<List<MovementRow>> ReadMovementsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        return ReadRowsAsync(
            connection,
            "SELECT \"Id\", \"Type\", \"ItemId\", \"FromLocationId\", \"ToLocationId\", \"LotId\", \"SerialNumber\", \"Quantity\", \"UserId\", \"ReferenceNumber\", \"Notes\", \"Timestamp\", \"CreatedAt\", \"UpdatedAt\" FROM \"Movements\" ORDER BY \"Id\";",
            reader => new MovementRow(
                ReadInt32(reader, "Id"),
                ReadInt32(reader, "Type"),
                ReadInt32(reader, "ItemId"),
                ReadNullableInt32(reader, "FromLocationId"),
                ReadNullableInt32(reader, "ToLocationId"),
                ReadNullableInt32(reader, "LotId"),
                ReadNullableString(reader, "SerialNumber"),
                ReadDecimal(reader, "Quantity"),
                ReadString(reader, "UserId"),
                ReadNullableString(reader, "ReferenceNumber"),
                ReadNullableString(reader, "Notes"),
                ReadUtc(reader, "Timestamp"),
                ReadUtc(reader, "CreatedAt"),
                ReadNullableUtc(reader, "UpdatedAt")),
            cancellationToken);
    }

    private static async Task<List<T>> ReadRowsAsync<T>(
        SqliteConnection connection,
        string commandText,
        Func<DbDataReader, T> projector,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(projector(reader));
        }

        return rows;
    }

    private static List<string> ValidateRelationships(SourceSnapshot snapshot)
    {
        var errors = new List<string>();
        ValidateUniqueIds("Items", snapshot.Items.Select(row => row.Id), errors);
        ValidateUniqueIds("ItemBarcodes", snapshot.Barcodes.Select(row => row.Id), errors);
        ValidateUniqueIds("Warehouses", snapshot.Warehouses.Select(row => row.Id), errors);
        ValidateUniqueIds("Locations", snapshot.Locations.Select(row => row.Id), errors);
        ValidateUniqueIds("Lots", snapshot.Lots.Select(row => row.Id), errors);
        ValidateUniqueIds("Stock", snapshot.Stock.Select(row => row.Id), errors);
        ValidateUniqueIds("Movements", snapshot.Movements.Select(row => row.Id), errors);

        var itemIds = snapshot.Items.Select(row => row.Id).ToHashSet();
        var warehouseIds = snapshot.Warehouses.Select(row => row.Id).ToHashSet();
        var locationIds = snapshot.Locations.Select(row => row.Id).ToHashSet();
        var lotIds = snapshot.Lots.Select(row => row.Id).ToHashSet();

        foreach (var row in snapshot.Barcodes.Where(row => !itemIds.Contains(row.ItemId)))
        {
            errors.Add($"ItemBarcodes {row.Id} references missing item {row.ItemId}");
        }

        foreach (var row in snapshot.Locations)
        {
            if (!warehouseIds.Contains(row.WarehouseId))
            {
                errors.Add($"Location {row.Id} references missing warehouse {row.WarehouseId}");
            }

            if (row.ParentLocationId == row.Id)
            {
                errors.Add($"Location {row.Id} cannot be its own parent");
            }
            else if (row.ParentLocationId.HasValue && !locationIds.Contains(row.ParentLocationId.Value))
            {
                errors.Add($"Location {row.Id} references missing parent location {row.ParentLocationId.Value}");
            }
        }

        var locationById = snapshot.Locations.ToDictionary(row => row.Id);
        foreach (var location in snapshot.Locations)
        {
            var visited = new HashSet<int>();
            var current = location;
            while (current.ParentLocationId.HasValue && locationById.TryGetValue(current.ParentLocationId.Value, out var parent))
            {
                if (!visited.Add(parent.Id))
                {
                    errors.Add($"Location hierarchy contains a cycle involving location {location.Id}");
                    break;
                }

                current = parent;
            }
        }

        foreach (var row in snapshot.Lots.Where(row => !itemIds.Contains(row.ItemId)))
        {
            errors.Add($"Lot {row.Id} references missing item {row.ItemId}");
        }

        foreach (var row in snapshot.Stock)
        {
            if (!itemIds.Contains(row.ItemId))
            {
                errors.Add($"Stock {row.Id} references missing item {row.ItemId}");
            }

            if (!locationIds.Contains(row.LocationId))
            {
                errors.Add($"Stock {row.Id} references missing location {row.LocationId}");
            }

            if (row.LotId.HasValue && !lotIds.Contains(row.LotId.Value))
            {
                errors.Add($"Stock {row.Id} references missing lot {row.LotId.Value}");
            }

            if (row.QuantityAvailable < 0 || row.QuantityReserved < 0)
            {
                errors.Add($"Stock {row.Id} contains a negative quantity");
            }
            else if (row.QuantityReserved > row.QuantityAvailable)
            {
                errors.Add($"Stock {row.Id} reserves more than its available quantity");
            }
        }

        foreach (var row in snapshot.Movements)
        {
            if (!itemIds.Contains(row.ItemId))
            {
                errors.Add($"Movement {row.Id} references missing item {row.ItemId}");
            }

            if (row.FromLocationId.HasValue && !locationIds.Contains(row.FromLocationId.Value))
            {
                errors.Add($"Movement {row.Id} references missing source location {row.FromLocationId.Value}");
            }

            if (row.ToLocationId.HasValue && !locationIds.Contains(row.ToLocationId.Value))
            {
                errors.Add($"Movement {row.Id} references missing target location {row.ToLocationId.Value}");
            }

            if (row.LotId.HasValue && !lotIds.Contains(row.LotId.Value))
            {
                errors.Add($"Movement {row.Id} references missing lot {row.LotId.Value}");
            }
        }

        return errors;
    }

    private static void ValidateUniqueIds(string table, IEnumerable<int> ids, List<string> errors)
    {
        foreach (var duplicateId in ids.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add($"{table} contains duplicate identifier {duplicateId}");
        }
    }

    private static object ReadValue(DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value is DBNull ? throw new InvalidOperationException($"Source column {column} contains NULL") : value;
    }

    private static string ReadString(DbDataReader reader, string column)
    {
        return Convert.ToString(ReadValue(reader, column), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string ReadStringOrEmpty(DbDataReader reader, string column)
    {
        return ReadNullableString(reader, column) ?? string.Empty;
    }

    private static string? ReadNullableString(DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int ReadInt32(DbDataReader reader, string column)
    {
        return Convert.ToInt32(ReadValue(reader, column), CultureInfo.InvariantCulture);
    }

    private static int? ReadNullableInt32(DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value is DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool ReadBoolean(DbDataReader reader, string column)
    {
        return Convert.ToBoolean(ReadValue(reader, column), CultureInfo.InvariantCulture);
    }

    private static decimal ReadDecimal(DbDataReader reader, string column)
    {
        return Convert.ToDecimal(ReadValue(reader, column), CultureInfo.InvariantCulture);
    }

    private static DateTime ReadUtc(DbDataReader reader, string column)
    {
        var value = ReadValue(reader, column);
        var dateTime = value switch
        {
            DateTime date => date,
            DateTimeOffset offset => offset.UtcDateTime,
            _ => DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
        };

        return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
    }

    private static DateTime? ReadNullableUtc(DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        if (value is DBNull)
        {
            return null;
        }

        var dateTime = value switch
        {
            DateTime date => date,
            DateTimeOffset offset => offset.UtcDateTime,
            _ => DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
        };

        return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
    }

    private static DateTime? ReadNullableDate(DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        if (value is DBNull)
        {
            return null;
        }

        var dateTime = value switch
        {
            DateTime date => date,
            DateTimeOffset offset => offset.DateTime,
            _ => DateTime.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces)
        };

        return DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
    }
}
