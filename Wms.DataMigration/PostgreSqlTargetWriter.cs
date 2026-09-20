using System.Data;
using System.Data.Common;

namespace Wms.DataMigration;

internal static class PostgreSqlTargetWriter
{
    private static readonly string[] IdentityTables =
    [
        "Items",
        "ItemBarcodes",
        "Warehouses",
        "Locations",
        "Lots",
        "SerialNumbers",
        "SerialNumberMigrationConflicts",
        "Stock",
        "Movements",
        "InventoryBalances",
        "InventoryTransactions"
    ];

    public static async Task WriteAsync(
        DbConnection connection,
        DbTransaction transaction,
        SourceSnapshot source,
        CancellationToken cancellationToken)
    {
        foreach (var row in source.Items)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "Items" ("Id", "Sku", "Name", "Description", "UnitOfMeasure", "IsActive", "RequiresLot", "RequiresSerial", "ShelfLifeDays", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @Sku, @Name, @Description, @UnitOfMeasure, @IsActive, @RequiresLot, @RequiresSerial, @ShelfLifeDays, @CreatedAt, @UpdatedAt)
                ON CONFLICT ("Id") DO UPDATE SET
                    "Sku" = EXCLUDED."Sku",
                    "Name" = EXCLUDED."Name",
                    "Description" = EXCLUDED."Description",
                    "UnitOfMeasure" = EXCLUDED."UnitOfMeasure",
                    "IsActive" = EXCLUDED."IsActive",
                    "RequiresLot" = EXCLUDED."RequiresLot",
                    "RequiresSerial" = EXCLUDED."RequiresSerial",
                    "ShelfLifeDays" = EXCLUDED."ShelfLifeDays",
                    "CreatedAt" = EXCLUDED."CreatedAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("Sku", row.Sku),
                ("Name", row.Name),
                ("Description", row.Description),
                ("UnitOfMeasure", row.UnitOfMeasure),
                ("IsActive", row.IsActive),
                ("RequiresLot", row.RequiresLot),
                ("RequiresSerial", row.RequiresSerial),
                ("ShelfLifeDays", row.ShelfLifeDays),
                ("CreatedAt", row.CreatedAt),
                ("UpdatedAt", row.UpdatedAt));
        }

        foreach (var row in source.Barcodes)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "ItemBarcodes" ("Id", "Barcode", "ItemId")
                VALUES (@Id, @Barcode, @ItemId)
                ON CONFLICT ("Id") DO UPDATE SET
                    "Barcode" = EXCLUDED."Barcode",
                    "ItemId" = EXCLUDED."ItemId";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("Barcode", row.Barcode),
                ("ItemId", row.ItemId));
        }

        foreach (var row in source.Warehouses)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "Warehouses" ("Id", "Code", "Name", "Address", "IsActive", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @Code, @Name, @Address, @IsActive, @CreatedAt, @UpdatedAt)
                ON CONFLICT ("Id") DO UPDATE SET
                    "Code" = EXCLUDED."Code",
                    "Name" = EXCLUDED."Name",
                    "Address" = EXCLUDED."Address",
                    "IsActive" = EXCLUDED."IsActive",
                    "CreatedAt" = EXCLUDED."CreatedAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("Code", row.Code),
                ("Name", row.Name),
                ("Address", row.Address),
                ("IsActive", row.IsActive),
                ("CreatedAt", row.CreatedAt),
                ("UpdatedAt", row.UpdatedAt));
        }

        foreach (var row in OrderLocations(source.Locations))
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "Locations" ("Id", "Code", "Name", "WarehouseId", "ParentLocationId", "IsPickable", "IsReceivable", "IsActive", "Capacity", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @Code, @Name, @WarehouseId, @ParentLocationId, @IsPickable, @IsReceivable, @IsActive, @Capacity, @CreatedAt, @UpdatedAt)
                ON CONFLICT ("Id") DO UPDATE SET
                    "Code" = EXCLUDED."Code",
                    "Name" = EXCLUDED."Name",
                    "WarehouseId" = EXCLUDED."WarehouseId",
                    "ParentLocationId" = EXCLUDED."ParentLocationId",
                    "IsPickable" = EXCLUDED."IsPickable",
                    "IsReceivable" = EXCLUDED."IsReceivable",
                    "IsActive" = EXCLUDED."IsActive",
                    "Capacity" = EXCLUDED."Capacity",
                    "CreatedAt" = EXCLUDED."CreatedAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("Code", row.Code),
                ("Name", row.Name),
                ("WarehouseId", row.WarehouseId),
                ("ParentLocationId", row.ParentLocationId),
                ("IsPickable", row.IsPickable),
                ("IsReceivable", row.IsReceivable),
                ("IsActive", row.IsActive),
                ("Capacity", row.Capacity),
                ("CreatedAt", row.CreatedAt),
                ("UpdatedAt", row.UpdatedAt));
        }

        foreach (var row in source.Lots)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "Lots" ("Id", "Number", "ItemId", "ExpiryDate", "ManufacturedDate", "RetestDate", "HoldUntil", "SupplierLotNumber", "Notes", "Status", "IsActive", "RecallReason", "RecalledAt", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @Number, @ItemId, @ExpiryDate, @ManufacturedDate, @RetestDate, @HoldUntil, @SupplierLotNumber, @Notes, @Status, @IsActive, @RecallReason, @RecalledAt, @CreatedAt, @UpdatedAt)
                ON CONFLICT ("Id") DO UPDATE SET
                    "Number" = EXCLUDED."Number",
                    "ItemId" = EXCLUDED."ItemId",
                    "ExpiryDate" = EXCLUDED."ExpiryDate",
                    "ManufacturedDate" = EXCLUDED."ManufacturedDate",
                    "RetestDate" = EXCLUDED."RetestDate",
                    "HoldUntil" = EXCLUDED."HoldUntil",
                    "SupplierLotNumber" = EXCLUDED."SupplierLotNumber",
                    "Notes" = EXCLUDED."Notes",
                    "Status" = EXCLUDED."Status",
                    "IsActive" = EXCLUDED."IsActive",
                    "RecallReason" = EXCLUDED."RecallReason",
                    "RecalledAt" = EXCLUDED."RecalledAt",
                    "CreatedAt" = EXCLUDED."CreatedAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("Number", row.Number),
                ("ItemId", row.ItemId),
                ("ExpiryDate", row.ExpiryDate),
                ("ManufacturedDate", row.ManufacturedDate),
                ("RetestDate", row.RetestDate),
                ("HoldUntil", row.HoldUntil),
                ("SupplierLotNumber", row.SupplierLotNumber),
                ("Notes", row.Notes),
                ("Status", row.Status),
                ("IsActive", row.IsActive),
                ("RecallReason", row.RecallReason),
                ("RecalledAt", row.RecalledAt),
                ("CreatedAt", row.CreatedAt),
                ("UpdatedAt", row.UpdatedAt));
        }

        foreach (var row in source.LegacySerialNumbers)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "SerialNumbers" ("Number", "ItemId", "LotId", "CurrentWarehouseId", "CurrentLocationId", "Status", "StatusReason", "HasMigrationConflict", "ConflictReason", "LastMovedAt", "CreatedAt")
                VALUES (@Number, @ItemId, @LotId, @CurrentWarehouseId, @CurrentLocationId, @Status, @StatusReason, @HasMigrationConflict, @ConflictReason, @LastMovedAt, @CreatedAt)
                ON CONFLICT ("ItemId", "Number") DO UPDATE SET
                    "LotId" = EXCLUDED."LotId",
                    "CurrentWarehouseId" = EXCLUDED."CurrentWarehouseId",
                    "CurrentLocationId" = EXCLUDED."CurrentLocationId",
                    "Status" = EXCLUDED."Status",
                    "StatusReason" = EXCLUDED."StatusReason",
                    "HasMigrationConflict" = EXCLUDED."HasMigrationConflict",
                    "ConflictReason" = EXCLUDED."ConflictReason",
                    "LastMovedAt" = EXCLUDED."LastMovedAt",
                    "CreatedAt" = EXCLUDED."CreatedAt";
                """,
                cancellationToken,
                ("Number", row.Number),
                ("ItemId", row.ItemId),
                ("LotId", row.LotId),
                ("CurrentWarehouseId", row.CurrentWarehouseId),
                ("CurrentLocationId", row.CurrentLocationId),
                ("Status", row.Status),
                ("StatusReason", row.StatusReason),
                ("HasMigrationConflict", row.HasMigrationConflict),
                ("ConflictReason", row.ConflictReason),
                ("LastMovedAt", row.LastMovedAt),
                ("CreatedAt", row.CreatedAt));
        }

        foreach (var row in source.LegacySerialConflicts)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "SerialNumberMigrationConflicts" ("ItemId", "Number", "SourceType", "SourceId", "Reason", "CreatedAt")
                SELECT @ItemId, @Number, @SourceType, @SourceId, @Reason, @CreatedAt
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "SerialNumberMigrationConflicts" existing
                    WHERE existing."ItemId" = @ItemId
                      AND existing."Number" = @Number
                      AND existing."SourceType" = @SourceType
                      AND existing."SourceId" IS NOT DISTINCT FROM @SourceId
                );
                """,
                cancellationToken,
                ("ItemId", row.ItemId),
                ("Number", row.Number),
                ("SourceType", row.SourceType),
                ("SourceId", row.SourceId),
                ("Reason", row.Reason),
                ("CreatedAt", row.CreatedAt));
        }

        foreach (var row in source.Stock)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "Stock" ("Id", "ItemId", "LocationId", "LotId", "SerialNumber", "QuantityAvailable", "QuantityReserved", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @ItemId, @LocationId, @LotId, @SerialNumber, @QuantityAvailable, @QuantityReserved, @CreatedAt, @UpdatedAt)
                ON CONFLICT ("Id") DO UPDATE SET
                    "ItemId" = EXCLUDED."ItemId",
                    "LocationId" = EXCLUDED."LocationId",
                    "LotId" = EXCLUDED."LotId",
                    "SerialNumber" = EXCLUDED."SerialNumber",
                    "QuantityAvailable" = EXCLUDED."QuantityAvailable",
                    "QuantityReserved" = EXCLUDED."QuantityReserved",
                    "CreatedAt" = EXCLUDED."CreatedAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("ItemId", row.ItemId),
                ("LocationId", row.LocationId),
                ("LotId", row.LotId),
                ("SerialNumber", NormalizeLegacySerial(row.SerialNumber)),
                ("QuantityAvailable", row.QuantityAvailable),
                ("QuantityReserved", row.QuantityReserved),
                ("CreatedAt", row.CreatedAt),
                ("UpdatedAt", row.UpdatedAt));
        }

        foreach (var row in source.Movements)
        {
            await ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO "Movements" ("Id", "Type", "ItemId", "FromLocationId", "ToLocationId", "LotId", "SerialNumber", "Quantity", "UserId", "ReferenceNumber", "Notes", "Timestamp", "CreatedAt", "UpdatedAt")
                VALUES (@Id, @Type, @ItemId, @FromLocationId, @ToLocationId, @LotId, @SerialNumber, @Quantity, @UserId, @ReferenceNumber, @Notes, @Timestamp, @CreatedAt, @UpdatedAt)
                ON CONFLICT ("Id") DO UPDATE SET
                    "Type" = EXCLUDED."Type",
                    "ItemId" = EXCLUDED."ItemId",
                    "FromLocationId" = EXCLUDED."FromLocationId",
                    "ToLocationId" = EXCLUDED."ToLocationId",
                    "LotId" = EXCLUDED."LotId",
                    "SerialNumber" = EXCLUDED."SerialNumber",
                    "Quantity" = EXCLUDED."Quantity",
                    "UserId" = EXCLUDED."UserId",
                    "ReferenceNumber" = EXCLUDED."ReferenceNumber",
                    "Notes" = EXCLUDED."Notes",
                    "Timestamp" = EXCLUDED."Timestamp",
                    "CreatedAt" = EXCLUDED."CreatedAt",
                    "UpdatedAt" = EXCLUDED."UpdatedAt";
                """,
                cancellationToken,
                ("Id", row.Id),
                ("Type", row.Type),
                ("ItemId", row.ItemId),
                ("FromLocationId", row.FromLocationId),
                ("ToLocationId", row.ToLocationId),
                ("LotId", row.LotId),
                ("SerialNumber", NormalizeLegacySerial(row.SerialNumber)),
                ("Quantity", row.Quantity),
                ("UserId", row.UserId),
                ("ReferenceNumber", row.ReferenceNumber),
                ("Notes", row.Notes),
                ("Timestamp", row.Timestamp),
                ("CreatedAt", row.CreatedAt),
                ("UpdatedAt", row.UpdatedAt));
        }

        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE "Stock" s
            SET "SerialNumberId" = serial."Id"
            FROM "SerialNumbers" serial
            WHERE s."SerialNumber" IS NOT NULL
              AND serial."ItemId" = s."ItemId"
              AND serial."Number" = left(upper(btrim(s."SerialNumber")), 100)
              AND serial."HasMigrationConflict" = FALSE;

            UPDATE "Movements" m
            SET "SerialNumberId" = serial."Id"
            FROM "SerialNumbers" serial
            WHERE m."SerialNumber" IS NOT NULL
              AND serial."ItemId" = m."ItemId"
              AND serial."Number" = left(upper(btrim(m."SerialNumber")), 100)
              AND serial."HasMigrationConflict" = FALSE;
            """,
            cancellationToken);

        // The EF cutover migration runs before this importer and therefore
        // cannot see legacy Stock rows that are imported later. Seed the
        // same immutable opening snapshot here, idempotently, so a fresh
        // SQLite-to-PostgreSQL target reconciles immediately after import.
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO "InventoryBalances"
            (
                "WarehouseId", "LocationId", "ItemId", "LotId",
                "SerialNumberId", "SerialNumber", "LicensePlateId",
                "InventoryStatusId", "BaseUnitOfMeasure", "OnHandQuantity",
                "ReservedQuantity", "Revision", "CreatedAt", "UpdatedAt"
            )
            SELECT
                l."WarehouseId", s."LocationId", s."ItemId", s."LotId",
                s."SerialNumberId", s."SerialNumber", s."LicensePlateId",
                s."InventoryStatusId", i."UnitOfMeasure", s."QuantityAvailable",
                s."QuantityReserved", 0, s."CreatedAt", s."UpdatedAt"
            FROM "Stock" s
            INNER JOIN "Locations" l ON l."Id" = s."LocationId"
            INNER JOIN "Items" i ON i."Id" = s."ItemId"
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO "InventoryTransactions"
            (
                "WarehouseId", "LocationId", "ItemId", "LotId",
                "SerialNumberId", "SerialNumber", "LicensePlateId",
                "InventoryStatusId", "BaseUnitOfMeasure", "Type",
                "QuantityDelta", "QuantityBefore", "QuantityAfter",
                "ReservedQuantityDelta", "ReservedQuantityBefore",
                "ReservedQuantityAfter", "ReferenceType", "ReferenceId",
                "ReferenceLine", "Reason", "ActorUserId", "OccurredAtUtc",
                "CorrelationId", "IdempotencyKey", "TransactionGroupId",
                "EntrySequence", "MovementId", "ReversalOfTransactionId",
                "CreatedAt", "UpdatedAt"
            )
            SELECT
                l."WarehouseId", s."LocationId", s."ItemId", s."LotId",
                s."SerialNumberId", s."SerialNumber", s."LicensePlateId",
                s."InventoryStatusId", i."UnitOfMeasure", 1,
                s."QuantityAvailable", 0, s."QuantityAvailable",
                s."QuantityReserved", 0, s."QuantityReserved",
                'Stock', CAST(s."Id" AS text), NULL,
                'Legacy stock balance carried into the inventory ledger',
                'system.migration', s."CreatedAt",
                ('legacy:stock:' || CAST(s."Id" AS text)),
                ('legacy:stock:' || CAST(s."Id" AS text)),
                ('legacy:stock:' || CAST(s."Id" AS text)), 1, NULL, NULL,
                s."CreatedAt", NULL
            FROM "Stock" s
            INNER JOIN "Locations" l ON l."Id" = s."LocationId"
            INNER JOIN "Items" i ON i."Id" = s."ItemId"
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM "InventoryTransactions" existing
                WHERE existing."IdempotencyKey" = ('legacy:stock:' || CAST(s."Id" AS text))
                  AND existing."EntrySequence" = 1
            );
            """,
            cancellationToken);

        foreach (var table in IdentityTables)
        {
            await ExecuteAsync(
                connection,
                transaction,
                $"""SELECT setval(pg_get_serial_sequence('"{table}"', 'Id'), COALESCE(MAX("Id"), 1), MAX("Id") IS NOT NULL) FROM "{table}";""",
                cancellationToken);
        }
    }

    public static async Task<DataMigrationSummary> ReadSummaryAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var rowCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in new[] { "Items", "ItemBarcodes", "Warehouses", "Locations", "Lots", "SerialNumbers", "SerialNumberMigrationConflicts", "Stock", "Movements", "InventoryBalances", "InventoryTransactions" })
        {
            rowCounts[table] = await ReadInt64Async(
                connection,
                transaction,
                $"""SELECT COUNT(*) FROM "{table}";""",
                cancellationToken);
        }

        return new DataMigrationSummary(
            rowCounts,
            await ReadDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(\"QuantityAvailable\"), 0) FROM \"Stock\";", cancellationToken),
            await ReadDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(\"QuantityReserved\"), 0) FROM \"Stock\";", cancellationToken),
            await ReadDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(\"Quantity\"), 0) FROM \"Movements\";", cancellationToken));
    }

    public static async Task<IReadOnlyList<string>> ValidateRelationshipsAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["orphan item barcodes"] = "SELECT COUNT(*) FROM \"ItemBarcodes\" b LEFT JOIN \"Items\" i ON i.\"Id\" = b.\"ItemId\" WHERE i.\"Id\" IS NULL;",
            ["orphan locations"] = "SELECT COUNT(*) FROM \"Locations\" l LEFT JOIN \"Warehouses\" w ON w.\"Id\" = l.\"WarehouseId\" LEFT JOIN \"Locations\" p ON p.\"Id\" = l.\"ParentLocationId\" WHERE w.\"Id\" IS NULL OR (l.\"ParentLocationId\" IS NOT NULL AND p.\"Id\" IS NULL) OR l.\"ParentLocationId\" = l.\"Id\";",
            ["orphan lots"] = "SELECT COUNT(*) FROM \"Lots\" l LEFT JOIN \"Items\" i ON i.\"Id\" = l.\"ItemId\" WHERE i.\"Id\" IS NULL;",
            ["orphan serial numbers"] = "SELECT COUNT(*) FROM \"SerialNumbers\" s LEFT JOIN \"Items\" i ON i.\"Id\" = s.\"ItemId\" LEFT JOIN \"Locations\" l ON l.\"Id\" = s.\"CurrentLocationId\" LEFT JOIN \"Lots\" o ON o.\"Id\" = s.\"LotId\" WHERE i.\"Id\" IS NULL OR (s.\"CurrentLocationId\" IS NOT NULL AND l.\"Id\" IS NULL) OR (s.\"LotId\" IS NOT NULL AND o.\"Id\" IS NULL);",
            ["orphan serial conflicts"] = "SELECT COUNT(*) FROM \"SerialNumberMigrationConflicts\" c LEFT JOIN \"Items\" i ON i.\"Id\" = c.\"ItemId\" WHERE i.\"Id\" IS NULL;",
            ["orphan stock"] = "SELECT COUNT(*) FROM \"Stock\" s LEFT JOIN \"Items\" i ON i.\"Id\" = s.\"ItemId\" LEFT JOIN \"Locations\" l ON l.\"Id\" = s.\"LocationId\" LEFT JOIN \"Lots\" o ON o.\"Id\" = s.\"LotId\" LEFT JOIN \"SerialNumbers\" sn ON sn.\"Id\" = s.\"SerialNumberId\" WHERE i.\"Id\" IS NULL OR l.\"Id\" IS NULL OR (s.\"LotId\" IS NOT NULL AND o.\"Id\" IS NULL) OR (s.\"SerialNumberId\" IS NOT NULL AND sn.\"Id\" IS NULL);",
            ["orphan movements"] = "SELECT COUNT(*) FROM \"Movements\" m LEFT JOIN \"Items\" i ON i.\"Id\" = m.\"ItemId\" LEFT JOIN \"Locations\" f ON f.\"Id\" = m.\"FromLocationId\" LEFT JOIN \"Locations\" t ON t.\"Id\" = m.\"ToLocationId\" LEFT JOIN \"Lots\" o ON o.\"Id\" = m.\"LotId\" LEFT JOIN \"SerialNumbers\" sn ON sn.\"Id\" = m.\"SerialNumberId\" WHERE i.\"Id\" IS NULL OR (m.\"FromLocationId\" IS NOT NULL AND f.\"Id\" IS NULL) OR (m.\"ToLocationId\" IS NOT NULL AND t.\"Id\" IS NULL) OR (m.\"LotId\" IS NOT NULL AND o.\"Id\" IS NULL) OR (m.\"SerialNumberId\" IS NOT NULL AND sn.\"Id\" IS NULL);",
            ["stock without inventory balance"] = """
                SELECT COUNT(*)
                FROM "Stock" s
                INNER JOIN "Locations" l ON l."Id" = s."LocationId"
                INNER JOIN "Items" i ON i."Id" = s."ItemId"
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM "InventoryBalances" b
                    WHERE b."WarehouseId" = l."WarehouseId"
                      AND b."LocationId" = s."LocationId"
                      AND b."ItemId" = s."ItemId"
                      AND b."LotId" IS NOT DISTINCT FROM s."LotId"
                      AND b."SerialNumberId" IS NOT DISTINCT FROM s."SerialNumberId"
                      AND b."SerialNumber" IS NOT DISTINCT FROM s."SerialNumber"
                      AND b."LicensePlateId" IS NOT DISTINCT FROM s."LicensePlateId"
                      AND b."InventoryStatusId" = s."InventoryStatusId"
                      AND b."BaseUnitOfMeasure" = i."UnitOfMeasure"
                );
                """,
            ["stock without legacy opening ledger row"] = """
                SELECT COUNT(*)
                FROM "Stock" s
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM "InventoryTransactions" t
                    WHERE t."IdempotencyKey" = ('legacy:stock:' || CAST(s."Id" AS text))
                      AND t."EntrySequence" = 1
                );
                """
        };

        var errors = new List<string>();
        foreach (var (description, sql) in checks)
        {
            var count = await ReadInt64Async(connection, transaction, sql, cancellationToken);
            if (count > 0)
            {
                errors.Add($"target contains {count} {description}");
            }
        }

        return errors;
    }

    private static List<LocationRow> OrderLocations(IReadOnlyList<LocationRow> locations)
    {
        var remaining = locations.ToDictionary(row => row.Id);
        var ordered = new List<LocationRow>(locations.Count);

        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(row => !row.ParentLocationId.HasValue || !remaining.ContainsKey(row.ParentLocationId.Value))
                .OrderBy(row => row.Id)
                .ToArray();

            if (ready.Length == 0)
            {
                throw new InvalidOperationException("Location hierarchy cannot be ordered because it contains a cycle.");
            }

            foreach (var row in ready)
            {
                ordered.Add(row);
                remaining.Remove(row.Id);
            }
        }

        return ordered;
    }

    private static string? NormalizeLegacySerial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string commandText,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadInt64Async(
        DbConnection connection,
        DbTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<decimal> ReadDecimalAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        parameter.DbType = value switch
        {
            int => DbType.Int32,
            long => DbType.Int64,
            bool => DbType.Boolean,
            decimal => DbType.Decimal,
            string => DbType.String,
            _ => parameter.DbType
        };
        command.Parameters.Add(parameter);
    }
}
