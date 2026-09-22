using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.Integration;

[Collection(PostgreSqlTestFixture.Name)]
public sealed class PostgreSqlIntegrationTests_Harness(PostgreSqlTestDatabase database)
{
    [PostgreSqlFact]
    public async Task FreshIsolatedSchemaHasAllMigrationsApplied()
    {
        await using var context = database.CreateContext();

        Assert.Contains("Npgsql", context.Database.ProviderName, StringComparison.Ordinal);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.True(await context.Database.CanConnectAsync());
        var migrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(migrations.Length, appliedMigrations.Length);
    }

    [PostgreSqlFact]
    public async Task ConstraintsPrecisionUtcAndRollbackAreEnforced()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGH-{token}", "PostgreSQL Harness Warehouse");
        var item = new Item($"PGH-{token}", "PostgreSQL Harness Item", "EA");
        context.AddRange(warehouse, item);
        await context.SaveChangesAsync();

        var duplicateWarehouse = new Warehouse(warehouse.Code, "Duplicate Warehouse");
        context.Warehouses.Add(duplicateWarehouse);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var work = new WarehouseWorkEntity(
            $"WORK-PGH-{token}",
            $"create-{token}",
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            token);
        work.AddLine(new WarehouseWorkLine(1, warehouse.Id, item.Id, 12.345678901234m, "EA"));
        work.MakeAvailable(DateTime.UtcNow);
        context.WarehouseWorks.Add(work);
        await context.SaveChangesAsync();

        work.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        work.Lines.Single().PlannedQuantity.Should().Be(12.345678901234m);

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Warehouses.Add(new Warehouse($"PG-RB-{token}", "Rollback Warehouse"));
        await context.SaveChangesAsync();
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();

        (await context.Warehouses.AnyAsync(value => value.Code == $"PG-RB-{token}"))
            .Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task RevisionTokenRejectsConcurrentWorkMutation()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGC-{token}", "Concurrency Warehouse");
        var item = new Item($"PGC-{token}", "Concurrency Item", "EA");
        seedContext.AddRange(warehouse, item);
        await seedContext.SaveChangesAsync();

        var work = new WarehouseWorkEntity(
            $"WORK-PGC-{token}",
            $"create-{token}",
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            token);
        work.AddLine(new WarehouseWorkLine(1, warehouse.Id, item.Id, 1m, "EA"));
        work.MakeAvailable(DateTime.UtcNow);
        seedContext.WarehouseWorks.Add(work);
        await seedContext.SaveChangesAsync();

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = await firstContext.WarehouseWorks.SingleAsync(value => value.Id == work.Id);
        var second = await secondContext.WarehouseWorks.SingleAsync(value => value.Id == work.Id);

        first.Assign("worker-a", null, "manager", DateTime.UtcNow);
        second.Assign("worker-b", null, "manager", DateTime.UtcNow);
        await firstContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondContext.SaveChangesAsync());
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
    }

    [PostgreSqlFact]
    public async Task LocationCodesAreWarehouseScopedAndDatabaseUnique()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGL-{token}", "Location uniqueness warehouse");
        var secondWarehouse = new Warehouse($"PGL2-{token}", "Second location uniqueness warehouse");
        seedContext.AddRange(firstWarehouse, secondWarehouse);
        await seedContext.SaveChangesAsync();

        await using var firstContext = database.CreateContext();
        await using var duplicateContext = database.CreateContext();
        await using var secondWarehouseContext = database.CreateContext();

        firstContext.Locations.Add(new Location($"LOC-{token}", "First location", firstWarehouse.Id));
        await firstContext.SaveChangesAsync();

        duplicateContext.Locations.Add(new Location($" loc-{token} ", "Duplicate location", firstWarehouse.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());

        secondWarehouseContext.Locations.Add(new Location($"LOC-{token}", "Second warehouse location", secondWarehouse.Id));
        await secondWarehouseContext.SaveChangesAsync();

        Assert.Equal(
            2,
            await seedContext.Locations
                .Where(location => location.Code == $"LOC-{token}")
            .CountAsync());
    }

    [PostgreSqlFact]
    public async Task ItemSkuAndBarcodeIndexesRejectDuplicateRows()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var first = new Item($"PGI-{token}", "Item uniqueness source", "EA");
        first.AddBarcode($"BC-{token}");
        seedContext.Items.Add(first);
        await seedContext.SaveChangesAsync();

        await using var duplicateSkuContext = database.CreateContext();
        duplicateSkuContext.Items.Add(new Item($"pgi-{token}", "Duplicate SKU", "EA"));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateSkuContext.SaveChangesAsync());

        await using var duplicateBarcodeContext = database.CreateContext();
        var duplicateBarcode = new Item($"PGI2-{token}", "Duplicate barcode", "EA");
        duplicateBarcode.AddBarcode($" bc-{token} ");
        duplicateBarcodeContext.Items.Add(duplicateBarcode);
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateBarcodeContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task UomConversionPrecisionAndVersionUniquenessArePersisted()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var item = new Item($"PGU-{token}", "UOM precision item", "EA");
        var each = new UnitOfMeasure($"EA-{token}", UnitOfMeasureCategory.Count, 12, "ea", "Each");
        var caseUnit = new UnitOfMeasure($"CS-{token}", UnitOfMeasureCategory.Count, 12, "case", "Case");
        context.AddRange(item, each, caseUnit);
        await context.SaveChangesAsync();

        var conversion = new ItemUnitConversion(
            item.Id,
            caseUnit.Code,
            each.Code,
            12.345678901234m,
            12,
            QuantityRoundingMode.ToEven);
        context.ItemUnitConversions.Add(conversion);
        await context.SaveChangesAsync();

        var saved = await context.ItemUnitConversions.SingleAsync();
        Assert.Equal(12.345678901234m, saved.ConversionFactor);
        Assert.Equal(12, saved.ResultPrecision);

        context.ItemUnitConversions.Add(new ItemUnitConversion(
            item.Id,
            caseUnit.Code,
            each.Code,
            10m,
            12,
            version: 1));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task PackagingGtinAndBarcodeIndexesRejectDuplicateDefinitions()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var source = new Item($"PGP-{token}", "Packaging source", "EA");
        source.AddPackaging(new ItemPackaging(
            "CASE",
            "EA",
            12m,
            barcode: $"PKG-{token}",
            lengthCm: 40m,
            widthCm: 30m,
            heightCm: 20m,
            gtin: "00012345678905",
            type: PackagingType.Case));
        context.Items.Add(source);
        await context.SaveChangesAsync();

        var saved = await context.ItemPackagings.SingleAsync();
        Assert.Equal(0.024m, saved.VolumeCubicMeters);
        Assert.Equal("00012345678905", saved.Gtin);

        var duplicate = new Item($"PGP2-{token}", "Duplicate packaging", "EA");
        duplicate.AddPackaging(new ItemPackaging(
            "CASE",
            "EA",
            12m,
            barcode: $"PKG2-{token}",
            gtin: "00012345678905"));
        context.Items.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task GlobalIdentifierIndexRejectsAmbiguousNormalizedValues()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var value = $"SCAN-{token}";
        context.WmsIdentifiers.Add(new WmsIdentifier(
            value,
            value,
            IdentificationKind.Item,
            BarcodeSymbology.Code128,
            $"ITEM:{token}"));
        await context.SaveChangesAsync();

        context.WmsIdentifiers.Add(new WmsIdentifier(
            value.ToLowerInvariant(),
            value.ToLowerInvariant(),
            IdentificationKind.Location,
            BarcodeSymbology.Code128,
            $"LOCATION:{token}"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task LotAndSerialIdentityIndexesRejectNormalizedDuplicatesPerItem()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var item = new Item(
            $"PGLS-{token}",
            "Lot and serial identity item",
            "EA",
            requiresLot: true,
            requiresSerial: true);
        seedContext.Items.Add(item);
        await seedContext.SaveChangesAsync();

        await using (var lotContext = database.CreateContext())
        {
            lotContext.Lots.AddRange(
                new Lot($"LOT-{token}", item.Id),
                new Lot($" lot-{token} ", item.Id));

            await Assert.ThrowsAsync<DbUpdateException>(() => lotContext.SaveChangesAsync());
        }

        await using (var serialContext = database.CreateContext())
        {
            serialContext.SerialNumbers.AddRange(
                new SerialNumber($"SERIAL-{token}", item.Id),
                new SerialNumber($" serial-{token} ", item.Id));

            await Assert.ThrowsAsync<DbUpdateException>(() => serialContext.SaveChangesAsync());
        }
    }

    [PostgreSqlFact]
    public async Task InventoryStatusCodesAreWarehouseScopedAndUniqueAtTheDatabaseBoundary()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGS1-{token}", "First status warehouse");
        var secondWarehouse = new Warehouse($"PGS2-{token}", "Second status warehouse");
        seedContext.Warehouses.AddRange(firstWarehouse, secondWarehouse);
        await seedContext.SaveChangesAsync();

        seedContext.InventoryStatuses.Add(new InventoryStatus(
            $"CUSTOM-{token}",
            "Custom status",
            "حالة مخصصة",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true,
            warehouseId: firstWarehouse.Id));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.InventoryStatuses.Add(new InventoryStatus(
                $" custom-{token} ",
                "Duplicate custom status",
                "حالة مخصصة مكررة",
                isAvailable: true,
                isAllocatable: true,
                isPickable: true,
                isShippable: true,
                isCountable: true,
                warehouseId: firstWarehouse.Id));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using (var secondWarehouseContext = database.CreateContext())
        {
            secondWarehouseContext.InventoryStatuses.Add(new InventoryStatus(
                $"custom-{token}",
                "Second warehouse custom status",
                "حالة مخصصة للمخزن الثاني",
                isAvailable: true,
                isAllocatable: true,
                isPickable: true,
                isShippable: true,
                isCountable: true,
                warehouseId: secondWarehouse.Id));

            await secondWarehouseContext.SaveChangesAsync();
        }
    }

    [PostgreSqlFact]
    public async Task LicensePlateNumbersRejectDuplicateSsccValuesAtTheDatabaseBoundary()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGLP-{token}", "License plate warehouse");
        seedContext.Warehouses.Add(warehouse);
        await seedContext.SaveChangesAsync();

        seedContext.LicensePlates.Add(new LicensePlate(
            "000123456789012343",
            LicensePlateType.Pallet,
            warehouse.Id,
            isSscc: true));
        await seedContext.SaveChangesAsync();

        await using var duplicateContext = database.CreateContext();
        duplicateContext.LicensePlates.Add(new LicensePlate(
            " 000123456789012343 ",
            LicensePlateType.Pallet,
            warehouse.Id,
            isSscc: true));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task LedgerBalanceAndIdempotencyIndexesRejectDuplicateRows()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGLG-{token}", "Ledger warehouse");
        var item = new Item($"PGLG-{token}", "Ledger item", "EA");
        seedContext.AddRange(warehouse, item);
        await seedContext.SaveChangesAsync();

        var location = new Location($"PGLG-{token}", "Ledger bin", warehouse.Id);
        seedContext.Locations.Add(location);
        await seedContext.SaveChangesAsync();

        var key = new InventoryBalanceKey(
            warehouse.Id,
            location.Id,
            item.Id,
            lotId: null,
            serialNumberId: null,
            serialNumber: null,
            licensePlateId: null,
            InventoryStatusSystemIds.Available,
            item.UnitOfMeasure);
        seedContext.InventoryBalances.Add(new InventoryBalance(key));
        await seedContext.SaveChangesAsync();

        await using (var duplicateBalanceContext = database.CreateContext())
        {
            duplicateBalanceContext.InventoryBalances.Add(new InventoryBalance(key));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateBalanceContext.SaveChangesAsync());
        }

        var occurredAt = DateTime.UtcNow;
        seedContext.InventoryTransactions.Add(new InventoryTransaction(
            InventoryTransactionType.Receipt,
            key,
            quantityDelta: 1m,
            quantityBefore: 0m,
            quantityAfter: 1m,
            reservedQuantityDelta: 0m,
            reservedQuantityBefore: 0m,
            reservedQuantityAfter: 0m,
            actorUserId: "postgres-test",
            occurredAtUtc: occurredAt,
            correlationId: $"CORR-{token}",
            idempotencyKey: $"IDEMP-{token}",
            transactionGroupId: $"GROUP-{token}",
            entrySequence: 1));
        await seedContext.SaveChangesAsync();

        await using var duplicateTransactionContext = database.CreateContext();
        duplicateTransactionContext.InventoryTransactions.Add(new InventoryTransaction(
            InventoryTransactionType.Receipt,
            key,
            quantityDelta: 1m,
            quantityBefore: 0m,
            quantityAfter: 1m,
            reservedQuantityDelta: 0m,
            reservedQuantityBefore: 0m,
            reservedQuantityAfter: 0m,
            actorUserId: "postgres-test",
            occurredAtUtc: occurredAt,
            correlationId: $"CORR2-{token}",
            idempotencyKey: $"IDEMP-{token}",
            transactionGroupId: $"GROUP2-{token}",
            entrySequence: 1));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateTransactionContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task InventoryCommandKeysAreUniqueWithinCallerScopeAtTheDatabaseBoundary()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var commandKey = $"COMMAND-{token}";

        context.InventoryCommandIdempotencies.Add(new InventoryCommandIdempotency(
            commandKey,
            "reserve",
            "scanner-1",
            $"HASH-{token}",
            $"CORR-{token}",
            "postgres-test",
            warehouseId: null,
            now,
            now.AddHours(1)));
        await context.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.InventoryCommandIdempotencies.Add(new InventoryCommandIdempotency(
                commandKey,
                "reserve",
                "scanner-1",
                $"HASH2-{token}",
                $"CORR2-{token}",
                "postgres-test",
                warehouseId: null,
                now,
                now.AddHours(1)));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using var otherCallerContext = database.CreateContext();
        otherCallerContext.InventoryCommandIdempotencies.Add(new InventoryCommandIdempotency(
            commandKey,
            "reserve",
            "integration-1",
            $"HASH3-{token}",
            $"CORR3-{token}",
            "postgres-test",
            warehouseId: null,
            now,
            now.AddHours(1)));
        await otherCallerContext.SaveChangesAsync();
    }

    [PostgreSqlFact]
    public async Task ReservationDemandKeysAreWarehouseScopedAndUniqueAtTheDatabaseBoundary()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGR1-{token}", "First reservation warehouse");
        var secondWarehouse = new Warehouse($"PGR2-{token}", "Second reservation warehouse");
        var item = new Item($"PGR-{token}", "Reservation item", "EA");
        seedContext.AddRange(firstWarehouse, secondWarehouse, item);
        await seedContext.SaveChangesAsync();

        seedContext.InventoryReservations.Add(new InventoryReservation(
            "sales-order",
            $"SO-{token}",
            demandLine: 1,
            warehouseId: firstWarehouse.Id,
            itemId: item.Id,
            requestedQuantity: 2m,
            mode: InventoryReservationMode.Hard,
            priority: 1,
            expiresAtUtc: null,
            actorUserId: "postgres-test",
            correlationId: $"CORR-{token}"));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.InventoryReservations.Add(new InventoryReservation(
                "sales-order",
                $"SO-{token}",
                demandLine: 1,
                warehouseId: firstWarehouse.Id,
                itemId: item.Id,
                requestedQuantity: 2m,
                mode: InventoryReservationMode.Hard,
                priority: 1,
                expiresAtUtc: null,
                actorUserId: "postgres-test",
                correlationId: $"CORR2-{token}"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using var secondWarehouseContext = database.CreateContext();
        secondWarehouseContext.InventoryReservations.Add(new InventoryReservation(
            "sales-order",
            $"SO-{token}",
            demandLine: 1,
            warehouseId: secondWarehouse.Id,
            itemId: item.Id,
            requestedQuantity: 2m,
            mode: InventoryReservationMode.Hard,
            priority: 1,
            expiresAtUtc: null,
            actorUserId: "postgres-test",
            correlationId: $"CORR3-{token}"));
        await secondWarehouseContext.SaveChangesAsync();
    }

    [PostgreSqlFact]
    public async Task SupplierCodeAndExternalIdentityIndexesRejectDuplicates()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        context.Suppliers.Add(new Supplier(
            $"SUP-{token}",
            "PostgreSQL supplier",
            externalErpIdentifier: $"ERP-{token}"));
        await context.SaveChangesAsync();

        await using (var duplicateCodeContext = database.CreateContext())
        {
            duplicateCodeContext.Suppliers.Add(new Supplier(
                $" sup-{token} ",
                "Duplicate supplier code",
                externalErpIdentifier: $"ERP2-{token}"));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateCodeContext.SaveChangesAsync());
        }

        await using var duplicateExternalContext = database.CreateContext();
        duplicateExternalContext.Suppliers.Add(new Supplier(
            $"SUP2-{token}",
            "Duplicate ERP identity",
            externalErpIdentifier: $" erp-{token} "));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateExternalContext.SaveChangesAsync());
    }

    [PostgreSqlFact]
    public async Task SerializedStockRejectsFractionalQuantityAtTheDatabaseBoundary()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGS-{token}", "Serial quantity warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var item = new Item($"PGS-{token}", "Serialized item", "EA", requiresSerial: true);
        var status = new InventoryStatus(
            $"SERIAL-{token}",
            "Serialized available",
            "متاح تسلسلي",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true,
            warehouseId: warehouse.Id);
        context.AddRange(item, status);
        await context.SaveChangesAsync();

        var location = new Location($"SERIAL-{token}", "Serial bin", warehouse.Id);
        var serial = new SerialNumber($"SN-{token}", item.Id);
        context.AddRange(location, serial);
        await context.SaveChangesAsync();

        context.Stock.Add(new Stock(
            item.Id,
            location.Id,
            new Quantity(0.5m),
            serialNumberId: serial.Id,
            inventoryStatusId: status.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
