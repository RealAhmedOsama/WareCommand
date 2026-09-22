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
    public async Task PutawayCreationKeyAllowsOneTaskPerWarehouse()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGW-{token}", "Putaway identity warehouse");
        var secondWarehouse = new Warehouse($"PGW2-{token}", "Second putaway identity warehouse");
        var item = new Item($"PGW-{token}", "Putaway identity item", "EA");
        seedContext.AddRange(firstWarehouse, secondWarehouse, item);
        await seedContext.SaveChangesAsync();

        var creationKey = $"receipt:putaway:{token}";
        var firstWork = new WarehouseWorkEntity(
            $"WORK-PGW-{token}",
            creationKey,
            WarehouseWorkType.Putaway,
            firstWarehouse.Id,
            "ReceiptLine",
            token);
        firstWork.AddLine(new WarehouseWorkLine(1, firstWarehouse.Id, item.Id, 5m, "EA"));
        firstWork.MakeAvailable(DateTime.UtcNow);
        seedContext.WarehouseWorks.Add(firstWork);
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            var duplicateWork = new WarehouseWorkEntity(
                $"WORK-PGW-DUP-{token}",
                creationKey,
                WarehouseWorkType.Putaway,
                firstWarehouse.Id,
                "ReceiptLine",
                $"duplicate-{token}");
            duplicateWork.AddLine(new WarehouseWorkLine(1, firstWarehouse.Id, item.Id, 5m, "EA"));
            duplicateWork.MakeAvailable(DateTime.UtcNow);
            duplicateContext.WarehouseWorks.Add(duplicateWork);

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        var secondWarehouseWork = new WarehouseWorkEntity(
            $"WORK-PGW-SECOND-{token}",
            creationKey,
            WarehouseWorkType.Putaway,
            secondWarehouse.Id,
            "ReceiptLine",
            $"second-{token}");
        secondWarehouseWork.AddLine(new WarehouseWorkLine(1, secondWarehouse.Id, item.Id, 5m, "EA"));
        secondWarehouseWork.MakeAvailable(DateTime.UtcNow);
        seedContext.WarehouseWorks.Add(secondWarehouseWork);
        await seedContext.SaveChangesAsync();

        (await seedContext.WarehouseWorks
            .CountAsync(work => work.CreationKey == creationKey))
            .Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task PutawayRuleCodesAreUniquePerWarehouse()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGR-{token}", "Putaway rule warehouse");
        var secondWarehouse = new Warehouse($"PGR2-{token}", "Second putaway rule warehouse");
        seedContext.AddRange(firstWarehouse, secondWarehouse);
        await seedContext.SaveChangesAsync();

        var code = $"rule-{token}";
        seedContext.PutawayRules.Add(new PutawayRule(
            firstWarehouse.Id,
            code,
            "Primary putaway rule",
            PutawayRuleStrategy.CapacityAware,
            priority: 100,
            DateTime.UtcNow.AddMinutes(-1)));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.PutawayRules.Add(new PutawayRule(
                firstWarehouse.Id,
                $" {code.ToUpperInvariant()} ",
                "Duplicate putaway rule",
                PutawayRuleStrategy.CapacityAware,
                priority: 90,
                DateTime.UtcNow.AddMinutes(-1)));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        seedContext.PutawayRules.Add(new PutawayRule(
            secondWarehouse.Id,
            code,
            "Second warehouse putaway rule",
            PutawayRuleStrategy.CapacityAware,
            priority: 100,
            DateTime.UtcNow.AddMinutes(-1)));
        await seedContext.SaveChangesAsync();

        var normalizedCode = code.ToUpperInvariant();
        (await seedContext.PutawayRules.CountAsync(rule => rule.Code == normalizedCode))
            .Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task InboundExceptionIdempotencyKeysAreUniquePerWarehouse()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGE-{token}", "Inbound exception warehouse");
        var secondWarehouse = new Warehouse($"PGE2-{token}", "Second inbound exception warehouse");
        seedContext.AddRange(firstWarehouse, secondWarehouse);
        await seedContext.SaveChangesAsync();

        var idempotencyKey = $"scan-exception-{token}";
        seedContext.InboundExceptions.Add(new InboundException(
            firstWarehouse.Id,
            $"EX-{token}-1",
            idempotencyKey,
            InboundExceptionCode.DocumentMismatch,
            InboundExceptionSeverity.High,
            "Inbound document mismatch",
            createdByUserId: "postgres-test"));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.InboundExceptions.Add(new InboundException(
                firstWarehouse.Id,
                $"EX-{token}-2",
                idempotencyKey,
                InboundExceptionCode.DocumentMismatch,
                InboundExceptionSeverity.High,
                "Duplicate inbound document mismatch",
                createdByUserId: "postgres-test"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        seedContext.InboundExceptions.Add(new InboundException(
            secondWarehouse.Id,
            $"EX-{token}-3",
            idempotencyKey,
            InboundExceptionCode.DocumentMismatch,
            InboundExceptionSeverity.High,
            "Second warehouse document mismatch",
            createdByUserId: "postgres-test"));
        await seedContext.SaveChangesAsync();

        (await seedContext.InboundExceptions.CountAsync(exception => exception.IdempotencyKey == idempotencyKey))
            .Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task CustomerIdentifiersRejectDuplicateNormalizedValues()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var customer = new Customer(
            $"PGCUSTOMER-{token}",
            "Provider customer",
            externalErpIdentifier: $"ERP-{token}",
            externalChannelIdentifier: $"CHANNEL-{token}");
        seedContext.Customers.Add(customer);
        await seedContext.SaveChangesAsync();

        await using (var duplicateCodeContext = database.CreateContext())
        {
            duplicateCodeContext.Customers.Add(new Customer(
                $" pgcustomer-{token} ",
                "Duplicate customer code"));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateCodeContext.SaveChangesAsync());
        }

        await using (var duplicateErpContext = database.CreateContext())
        {
            duplicateErpContext.Customers.Add(new Customer(
                $"PGCUSTOMER-ERP-{token}",
                "Duplicate ERP identifier",
                externalErpIdentifier: $" erp-{token} "));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateErpContext.SaveChangesAsync());
        }

        await using (var duplicateChannelContext = database.CreateContext())
        {
            duplicateChannelContext.Customers.Add(new Customer(
                $"PGCUSTOMER-CHANNEL-{token}",
                "Duplicate channel identifier",
                externalChannelIdentifier: $" channel-{token} "));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateChannelContext.SaveChangesAsync());
        }

        seedContext.Customers.Add(new Customer(
            $"PGCUSTOMER-NULL-{token}",
            "Customer without external identifiers"));
        await seedContext.SaveChangesAsync();
        (await seedContext.Customers.CountAsync(customer => customer.Code.Contains(token)))
            .Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task SalesOrderExternalReferencesAreUniquePerWarehouseAndSource()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGSO-{token}", "Sales order warehouse");
        var secondWarehouse = new Warehouse($"PGSO2-{token}", "Second sales order warehouse");
        var customer = new Customer($"PGSO-CUSTOMER-{token}", "Sales order customer");
        seedContext.AddRange(firstWarehouse, secondWarehouse, customer);
        await seedContext.SaveChangesAsync();

        var externalReference = $"external-{token}";
        seedContext.SalesOrders.Add(CreateSalesOrder(
            $"SO-{token}-1",
            firstWarehouse,
            customer,
            externalReference));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.SalesOrders.Add(CreateSalesOrder(
                $"SO-{token}-2",
                firstWarehouse,
                customer,
                $" {externalReference.ToUpperInvariant()} "));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        seedContext.SalesOrders.Add(CreateSalesOrder(
            $"SO-{token}-3",
            secondWarehouse,
            customer,
            externalReference));
        await seedContext.SaveChangesAsync();

        var normalizedExternalReference = externalReference.ToUpperInvariant();
        (await seedContext.SalesOrders.CountAsync(order => order.ExternalReference == normalizedExternalReference))
            .Should().Be(2);
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

    private static SalesOrder CreateSalesOrder(
        string documentNumber,
        Warehouse warehouse,
        Customer customer,
        string externalReference) => new(
        documentNumber,
        warehouse.Id,
        warehouse.Code,
        customer.Id,
        customer.Code,
        customer.LegalName,
        customer.LocalizedName,
        customer.ContactName,
        customer.ContactEmail,
        customer.ContactPhone,
        shipToAddressId: null,
        shipToCodeSnapshot: null,
        shipToRecipientNameSnapshot: null,
        shipToPhoneSnapshot: null,
        shipToCountryCodeSnapshot: null,
        shipToRegionSnapshot: null,
        shipToCitySnapshot: null,
        shipToPostalCodeSnapshot: null,
        shipToAddressLine1Snapshot: null,
        shipToAddressLine2Snapshot: null,
        shipToDeliveryInstructionsSnapshot: null,
        orderDate: DateOnly.FromDateTime(DateTime.UtcNow),
        requestedShipDate: null,
        externalReference,
        sourceType: "manual",
        sourceReference: null,
        priority: 100,
        defaultCarrierCodeSnapshot: null,
        defaultCarrierServiceCodeSnapshot: null,
        packagingProfileSnapshot: null,
        labelProfileSnapshot: null,
        allowPartialShipmentSnapshot: false,
        notes: null,
        createdByUserId: "postgres-test");

    [PostgreSqlFact]
    public async Task WarehouseWorkCommandKeysAreUniquePerOperationAndWork()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGWC-{token}", "Warehouse command ledger");
        var item = new Item($"PGWC-{token}", "Warehouse command item", "EA");
        seedContext.AddRange(warehouse, item);
        await seedContext.SaveChangesAsync();

        var firstWork = new WarehouseWorkEntity(
            $"WORK-PGWC-1-{token}",
            $"command-work-1-{token}",
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            token);
        firstWork.AddLine(new WarehouseWorkLine(1, warehouse.Id, item.Id, 1m, "EA"));
        var secondWork = new WarehouseWorkEntity(
            $"WORK-PGWC-2-{token}",
            $"command-work-2-{token}",
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            $"second-{token}");
        secondWork.AddLine(new WarehouseWorkLine(1, warehouse.Id, item.Id, 1m, "EA"));
        seedContext.WarehouseWorks.AddRange(firstWork, secondWork);
        await seedContext.SaveChangesAsync();

        var key = $"command-{token}";
        seedContext.WarehouseWorkCommands.Add(new WarehouseWorkCommand(
            firstWork.Id,
            "complete",
            key,
            "hash-1",
            "postgres-test",
            DateTime.UtcNow));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.WarehouseWorkCommands.Add(new WarehouseWorkCommand(
                firstWork.Id,
                "complete",
                key,
                "hash-1",
                "postgres-test",
                DateTime.UtcNow));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        seedContext.WarehouseWorkCommands.Add(new WarehouseWorkCommand(
            firstWork.Id,
            "start",
            key,
            "hash-2",
            "postgres-test",
            DateTime.UtcNow));
        seedContext.WarehouseWorkCommands.Add(new WarehouseWorkCommand(
            secondWork.Id,
            "complete",
            key,
            "hash-3",
            "postgres-test",
            DateTime.UtcNow));
        await seedContext.SaveChangesAsync();

        (await seedContext.WarehouseWorkCommands.CountAsync(command => command.IdempotencyKey == key))
            .Should().Be(3);
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
    public async Task ReceivingScanClientOperationKeysAreUniqueWithinTheirSession()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGRS-{token}", "Receiving-session warehouse");
        seedContext.Warehouses.Add(warehouse);
        await seedContext.SaveChangesAsync();

        var location = new Location($"PGRS-{token}", "Receiving-session location", warehouse.Id);
        seedContext.Locations.Add(location);
        await seedContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var session = new ReceivingSession(
            warehouse.Id,
            location.Id,
            ReceivingSessionSourceType.BlindReceipt,
            $"SESSION-{token}",
            "postgres-test",
            now,
            supervisorOverride: true,
            supervisorOverrideReason: "Provider qualification");
        session.AddScan(new ReceivingSessionScan(
            session,
            $"OP-{token}",
            $"ITEM-{token}",
            $"ITEM-{token}",
            1m,
            "EA",
            null,
            null,
            null,
            null,
            null,
            null,
            "postgres-test",
            now));
        seedContext.ReceivingSessions.Add(session);
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            var existingSession = await duplicateContext.ReceivingSessions
                .SingleAsync(value => value.Id == session.Id);
            duplicateContext.ReceivingSessionScans.Add(new ReceivingSessionScan(
                existingSession,
                $"OP-{token}",
                $"ITEM2-{token}",
                $"ITEM2-{token}",
                1m,
                "EA",
                null,
                null,
                null,
                null,
                null,
                null,
                "postgres-test",
                now));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using var otherSessionContext = database.CreateContext();
        var otherSession = new ReceivingSession(
            warehouse.Id,
            location.Id,
            ReceivingSessionSourceType.BlindReceipt,
            $"SESSION2-{token}",
            "postgres-test",
            now,
            supervisorOverride: true,
            supervisorOverrideReason: "Provider qualification");
        otherSession.AddScan(new ReceivingSessionScan(
            otherSession,
            $"OP-{token}",
            $"ITEM3-{token}",
            $"ITEM3-{token}",
            1m,
            "EA",
            null,
            null,
            null,
            null,
            null,
            null,
            "postgres-test",
            now));
        otherSessionContext.ReceivingSessions.Add(otherSession);
        await otherSessionContext.SaveChangesAsync();
    }

    [PostgreSqlFact]
    public async Task QualityInspectionIdentityIsUniquePerReceiptLineAndLicensePlate()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGQI-{token}", "Quality inspection warehouse");
        var item = new Item($"PGQI-{token}", "Quality inspection item", "EA");
        var status = new InventoryStatus(
            $"PGQI-{token}",
            "Quality inspection available",
            "متاح للفحص",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true,
            warehouseId: null,
            isSystem: true);
        seedContext.AddRange(warehouse, item, status);
        await seedContext.SaveChangesAsync();

        var location = new Location(
            $"PGQI-{token}",
            "Quality inspection receiving",
            warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        var firstLicensePlate = new LicensePlate(
            $"PGQI-LP1-{token}",
            LicensePlateType.Pallet,
            warehouse.Id);
        var secondLicensePlate = new LicensePlate(
            $"PGQI-LP2-{token}",
            LicensePlateType.Pallet,
            warehouse.Id);
        seedContext.AddRange(location, firstLicensePlate, secondLicensePlate);
        await seedContext.SaveChangesAsync();

        var receipt = new Receipt(
            $"PGQI-RECEIPT-{token}",
            warehouse.Id,
            warehouse.Code,
            supplierId: null,
            supplierCodeSnapshot: null,
            supplierNameSnapshot: null,
            purchaseOrderId: null,
            advanceShippingNoticeId: null,
            dockLocationId: null,
            receivingLocationId: location.Id,
            sourceType: "MANUAL",
            createdByUserId: "postgres-test");
        var line = new ReceiptLine(
            1,
            warehouse.Id,
            item.Id,
            item.Sku,
            item.Name,
            "EA",
            10m,
            "EA",
            10m,
            1m,
            0,
            QuantityRoundingMode.Reject,
            0m,
            "BASE",
            string.Empty,
            receivingLocationId: location.Id,
            inventoryStatusId: status.Id,
            inventoryStatusCodeSnapshot: status.Code,
            inventoryStatusNameSnapshot: status.Name);
        receipt.AddLine(line);
        receipt.Open("postgres-test", DateTime.UtcNow);
        seedContext.Receipts.Add(receipt);
        await seedContext.SaveChangesAsync();

        seedContext.QualityInspections.Add(new QualityInspection(
            $"QI-{token}-NULL-1",
            receipt.Id,
            line.Id,
            warehouse.Id,
            item.Id,
            item.Sku,
            item.Name,
            10m,
            10m,
            status.Id,
            sourceType: "MANUAL",
            createdByUserId: "postgres-test"));
        await seedContext.SaveChangesAsync();

        await using (var duplicateLineContext = database.CreateContext())
        {
            duplicateLineContext.QualityInspections.Add(new QualityInspection(
                $"QI-{token}-NULL-2",
                receipt.Id,
                line.Id,
                warehouse.Id,
                item.Id,
                item.Sku,
                item.Name,
                10m,
                10m,
                status.Id,
                sourceType: "MANUAL",
                createdByUserId: "postgres-test"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateLineContext.SaveChangesAsync());
        }

        seedContext.QualityInspections.Add(new QualityInspection(
            $"QI-{token}-LP1-1",
            receipt.Id,
            line.Id,
            warehouse.Id,
            item.Id,
            item.Sku,
            item.Name,
            10m,
            10m,
            status.Id,
            sourceType: "MANUAL",
            licensePlateId: firstLicensePlate.Id,
            createdByUserId: "postgres-test"));
        await seedContext.SaveChangesAsync();

        await using (var duplicateLicensePlateContext = database.CreateContext())
        {
            duplicateLicensePlateContext.QualityInspections.Add(new QualityInspection(
                $"QI-{token}-LP1-2",
                receipt.Id,
                line.Id,
                warehouse.Id,
                item.Id,
                item.Sku,
                item.Name,
                10m,
                10m,
                status.Id,
                sourceType: "MANUAL",
                licensePlateId: firstLicensePlate.Id,
                createdByUserId: "postgres-test"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateLicensePlateContext.SaveChangesAsync());
        }

        await using var otherLicensePlateContext = database.CreateContext();
        otherLicensePlateContext.QualityInspections.Add(new QualityInspection(
            $"QI-{token}-LP2-1",
            receipt.Id,
            line.Id,
            warehouse.Id,
            item.Id,
            item.Sku,
            item.Name,
            10m,
            10m,
            status.Id,
            sourceType: "MANUAL",
            licensePlateId: secondLicensePlate.Id,
            createdByUserId: "postgres-test"));
        await otherLicensePlateContext.SaveChangesAsync();
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
    public async Task PurchaseOrderExternalReferencesAreUniquePerSupplierAndSource()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGPO-{token}", "Purchase-order warehouse");
        var firstSupplier = new Supplier($"PGPO1-{token}", "First purchase-order supplier");
        var secondSupplier = new Supplier($"PGPO2-{token}", "Second purchase-order supplier");
        seedContext.AddRange(warehouse, firstSupplier, secondSupplier);
        await seedContext.SaveChangesAsync();

        seedContext.PurchaseOrders.Add(new PurchaseOrder(
            $"PO-{token}-1",
            warehouse.Id,
            warehouse.Code,
            firstSupplier.Id,
            firstSupplier.Code,
            firstSupplier.LegalName,
            DateOnly.FromDateTime(DateTime.UtcNow),
            externalReference: $" EXT-{token} ",
            sourceType: "EDI"));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.PurchaseOrders.Add(new PurchaseOrder(
                $"PO-{token}-2",
                warehouse.Id,
                warehouse.Code,
                firstSupplier.Id,
                firstSupplier.Code,
                firstSupplier.LegalName,
                DateOnly.FromDateTime(DateTime.UtcNow),
                externalReference: $"ext-{token}",
                sourceType: "edi"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using (var otherSupplierContext = database.CreateContext())
        {
            otherSupplierContext.PurchaseOrders.Add(new PurchaseOrder(
                $"PO-{token}-3",
                warehouse.Id,
                warehouse.Code,
                secondSupplier.Id,
                secondSupplier.Code,
                secondSupplier.LegalName,
                DateOnly.FromDateTime(DateTime.UtcNow),
                externalReference: $"EXT-{token}",
                sourceType: "EDI"));
            await otherSupplierContext.SaveChangesAsync();
        }

        await using var nullReferenceContext = database.CreateContext();
        nullReferenceContext.PurchaseOrders.AddRange(
            new PurchaseOrder(
                $"PO-{token}-4",
                warehouse.Id,
                warehouse.Code,
                firstSupplier.Id,
                firstSupplier.Code,
                firstSupplier.LegalName,
                DateOnly.FromDateTime(DateTime.UtcNow),
                sourceType: "EDI"),
            new PurchaseOrder(
                $"PO-{token}-5",
                warehouse.Id,
                warehouse.Code,
                firstSupplier.Id,
                firstSupplier.Code,
                firstSupplier.LegalName,
                DateOnly.FromDateTime(DateTime.UtcNow),
                sourceType: "EDI"));
        await nullReferenceContext.SaveChangesAsync();
    }

    [PostgreSqlFact]
    public async Task AdvanceShippingNoticeExternalReferencesAreUniquePerSupplierAndSource()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGASN-{token}", "ASN warehouse");
        var firstSupplier = new Supplier($"PGASN1-{token}", "First ASN supplier");
        var secondSupplier = new Supplier($"PGASN2-{token}", "Second ASN supplier");
        seedContext.AddRange(warehouse, firstSupplier, secondSupplier);
        await seedContext.SaveChangesAsync();

        seedContext.AdvanceShippingNotices.Add(new AdvanceShippingNotice(
            $"ASN-{token}-1",
            warehouse.Id,
            warehouse.Code,
            firstSupplier.Id,
            firstSupplier.Code,
            firstSupplier.LegalName,
            externalReference: $" EXT-{token} ",
            sourceType: "EDI"));
        await seedContext.SaveChangesAsync();

        await using (var duplicateContext = database.CreateContext())
        {
            duplicateContext.AdvanceShippingNotices.Add(new AdvanceShippingNotice(
                $"ASN-{token}-2",
                warehouse.Id,
                warehouse.Code,
                firstSupplier.Id,
                firstSupplier.Code,
                firstSupplier.LegalName,
                externalReference: $"ext-{token}",
                sourceType: "edi"));

            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }

        await using (var otherSupplierContext = database.CreateContext())
        {
            otherSupplierContext.AdvanceShippingNotices.Add(new AdvanceShippingNotice(
                $"ASN-{token}-3",
                warehouse.Id,
                warehouse.Code,
                secondSupplier.Id,
                secondSupplier.Code,
                secondSupplier.LegalName,
                externalReference: $"EXT-{token}",
                sourceType: "EDI"));
            await otherSupplierContext.SaveChangesAsync();
        }

        await using var nullReferenceContext = database.CreateContext();
        nullReferenceContext.AdvanceShippingNotices.AddRange(
            new AdvanceShippingNotice(
                $"ASN-{token}-4",
                warehouse.Id,
                warehouse.Code,
                firstSupplier.Id,
                firstSupplier.Code,
                firstSupplier.LegalName,
                sourceType: "EDI"),
            new AdvanceShippingNotice(
                $"ASN-{token}-5",
                warehouse.Id,
                warehouse.Code,
                firstSupplier.Id,
                firstSupplier.Code,
                firstSupplier.LegalName,
                sourceType: "EDI"));
        await nullReferenceContext.SaveChangesAsync();
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
