using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wms.Application.DataGeneration;
using Wms.Application.DependencyInjection;
using Wms.Application.DTOs;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.LicensePlates;
using Wms.Application.Packing;
using Wms.Application.Purchasing;
using Wms.Application.Returns;
using Wms.Application.SalesOrders;
using Wms.Application.Shipping;
using Wms.Application.Units;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Receiving;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Tests.Integration;

public sealed record DataGenerationRunReport(
    string GeneratorVersion,
    string Profile,
    string SeedFingerprint,
    string TargetIdentifier,
    long ElapsedMilliseconds,
    IReadOnlyDictionary<string, int> ActualCounts,
    IReadOnlyList<string> OperationOutcomes,
    string LogicalDatasetFingerprint,
    bool ReconciliationClean,
    int ReconciliationIssueCount,
    long ReconciliationTransactionsScanned);

public sealed class DataGenerationFixtureResult(
    DataGenerationRunReport report,
    DataGenerationActorCredentials actorCredentials)
{
    public DataGenerationRunReport Report { get; } = report;

    [JsonIgnore]
    public DataGenerationActorCredentials ActorCredentials { get; } = actorCredentials;
}

public sealed class DataGenerationActorCredentials(
    string userId,
    string userName,
    string password)
{
    [JsonIgnore]
    public string UserId { get; } = userId;

    [JsonIgnore]
    public string UserName { get; } = userName;

    [JsonIgnore]
    public string Password { get; } = password;

    public override string ToString() => "[ephemeral data-generation actor credentials]";
}

/// <summary>
/// Writes a repeatable, command-generated WMS scenario into a schema created by
/// PostgreSqlTestDatabase. Reference entities are inserted directly; inventory
/// and operational document changes go through the same application services
/// used by the host.
/// </summary>
public sealed class DeterministicPostgreSqlDataGenerationFixture
{
    private static readonly JsonSerializerOptions EvidenceSerializerOptions = new() { WriteIndented = true };
    private static readonly object EvidenceGate = new();
    private static readonly DateTimeOffset SeededAt =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static async Task<DataGenerationRunReport> WriteAsync(
        PostgreSqlTestDatabase target,
        DataGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        (await WriteWithActorCredentialsAsync(
            target,
            request,
            checkpointAsync: null,
            cancellationToken: cancellationToken)).Report;

    public static async Task<DataGenerationFixtureResult> WriteWithActorCredentialsAsync(
        PostgreSqlTestDatabase target,
        DataGenerationRequest request,
        Func<string, WmsDbContext, IInventoryReconciliationService, CancellationToken, Task>? checkpointAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        DiagnosticLoggerProvider.Clear();
        var plan = DataGenerationPlan.Create(request);
        ValidateIsolatedTarget(target);

        var writerConnectionBuilder = new NpgsqlConnectionStringBuilder(target.ScopedConnectionString!)
        {
            ApplicationName = "WareCommand.DataGeneration.Writer",
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = 8
        };
        using var poolCleanup = new ConnectionPoolCleanup(writerConnectionBuilder.ConnectionString);
        using var serviceProvider = BuildServiceProvider(writerConnectionBuilder.ConnectionString);
        using var scope = serviceProvider.CreateScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<WmsDbContext>();
        var reconciliationService = services.GetRequiredService<IInventoryReconciliationService>();
        await EnsureFreshTargetAsync(context, cancellationToken);

        async Task VerifyMutationAsync(string name)
        {
            if (checkpointAsync is null)
            {
                return;
            }

            context.ChangeTracker.Clear();
            await checkpointAsync(name, context, reconciliationService, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        var outcomes = new List<string>();
        var scenario = await CreateReferenceScenarioAsync(
            context,
            services,
            plan,
            cancellationToken);
        outcomes.Add("reference-data=created");

        var purchaseOrderService = services.GetRequiredService<IPurchaseOrderService>();
        var purchaseOrder = Require(await purchaseOrderService.CreateAsync(
            new PurchaseOrderInput(
                scenario.PrimaryWarehouse.Id,
                scenario.Suppliers[0].Id,
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 1, 22),
                ExternalReference: StableCode("POREF", request.Seed, "purchase-order"),
                SourceType: "DATA-GENERATION",
                Lines: [new PurchaseOrderLineInput(
                    scenario.Items[0].Sku,
                    20m,
                    "EA")]),
            scenario.Actor.Id,
            cancellationToken), "purchase-order:create");
        await VerifyMutationAsync("purchase-order-created");
        purchaseOrder = Require(await purchaseOrderService.ConfirmAsync(
            purchaseOrder.Id,
            scenario.Actor.Id,
            cancellationToken), "purchase-order:confirm");
        await VerifyMutationAsync("purchase-order-confirmed");
        outcomes.Add("purchase-order=created,confirmed");

        var inboundPlate = Require(await services.GetRequiredService<ILicensePlateService>().CreateAsync(
            new LicensePlateCreateInput(
                scenario.PrimaryWarehouse.Id,
                scenario.ReceivingLocation.Id,
                StableCode("LPN", request.Seed, "inbound-plate"),
                IsSscc: false,
                LicensePlateType.Pallet,
                SourceReference: purchaseOrder.DocumentNumber),
            scenario.Actor.Id,
            cancellationToken), "license-plate:inbound-create");
        await VerifyMutationAsync("inbound-license-plate-created");
        var receiptResult = Require(await services.GetRequiredService<IReceiveItemUseCase>().ExecuteAsync(
            new ReceiveItemDto(
                scenario.Items[0].Sku,
                scenario.ReceivingLocation.Code,
                20m,
                LotNumber: StableCode("LOT", request.Seed, "main-lot"),
                ExpiryDate: new DateTime(2027, 1, 15),
                ReferenceNumber: purchaseOrder.DocumentNumber,
                UnitOfMeasure: "EA",
                LicensePlateId: inboundPlate.Id,
                PurchaseOrderId: purchaseOrder.Id,
                PurchaseOrderLineId: purchaseOrder.Lines.Single().Id),
            scenario.Actor.Id,
            StableCode("IDEM", request.Seed, "purchase-receipt"),
            cancellationToken), "receipt:receive");
        await VerifyMutationAsync("purchase-receipt-received");
        Require(await services.GetRequiredService<IPutawayUseCase>().ExecuteAsync(
            new PutawayDto(
                scenario.Items[0].Sku,
                scenario.ReceivingLocation.Code,
                scenario.StorageLocation.Code,
                20m,
                LotNumber: StableCode("LOT", request.Seed, "main-lot"),
                UnitOfMeasure: "EA",
                LicensePlateId: inboundPlate.Id),
            scenario.Actor.Id,
            cancellationToken), "receipt:putaway");
        await VerifyMutationAsync("purchase-receipt-putaway");
        var receipt = Require(await services.GetRequiredService<Wms.Application.Receiving.IReceiptService>().GetAsync(
            receiptResult.ReceiptId!.Value,
            cancellationToken), "receipt:read");
        purchaseOrder = Require(await purchaseOrderService.GetAsync(purchaseOrder.Id, cancellationToken), "purchase-order:read");
        outcomes.Add("receipt=received,lot-tracked,lpn-linked,po-reconciled,putaway-completed");

        var lotId = await context.Lots.AsNoTracking()
            .Where(lot => lot.Number == receipt.Lines.Single().LotNumber)
            .Select(lot => (int?)lot.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The generated receipt did not create its expected lot.");

        await AddEdgeCaseDimensionsAsync(
            context,
            services,
            plan,
            scenario,
            request.Seed,
            outcomes,
            VerifyMutationAsync,
            cancellationToken);

        var salesOrderService = services.GetRequiredService<ISalesOrderService>();
        var salesOrder = Require(await salesOrderService.CreateAsync(
            new SalesOrderInput(
                scenario.PrimaryWarehouse.Id,
                scenario.Customers[0].Id,
                OrderDate: new DateOnly(2026, 1, 15),
                RequestedShipDate: new DateOnly(2026, 1, 16),
                ExternalReference: StableCode("SOREF", request.Seed, "sales-order"),
                SourceType: "DATA-GENERATION",
                DefaultCarrierCode: StableCode("CAR", request.Seed, "carrier"),
                DefaultCarrierServiceCode: "GROUND",
                AllowPartialShipment: false,
                Lines: [new SalesOrderLineInput(scenario.Items[0].Sku, 3m, "EA")]),
            scenario.Actor.Id,
            cancellationToken), "sales-order:create");
        await VerifyMutationAsync("sales-order-created");
        salesOrder = Require(await salesOrderService.ConfirmAsync(
            salesOrder.Id,
            scenario.Actor.Id,
            cancellationToken), "sales-order:confirm");
        await VerifyMutationAsync("sales-order-confirmed");
        var allocation = Require(await services.GetRequiredService<ISalesOrderAllocationService>().AllocateAsync(
            salesOrder.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: StableCode("ALLOC", request.Seed, "allocation")),
            scenario.Actor.Id,
            cancellationToken), "sales-order:allocate-and-release");
        await VerifyMutationAsync("sales-order-allocated-and-released");
        if (allocation.Work.Count != 1)
        {
            throw new InvalidOperationException("The seeded sales order did not produce one deterministic pick task.");
        }

        var workService = services.GetRequiredService<IWarehouseWorkService>();
        var pickWork = Require(await workService.GetAsync(allocation.Work[0].WorkId, cancellationToken), "pick-work:read");
        var pickLine = pickWork.Lines.Single();
        Require(await workService.AssignAsync(
            pickWork.Id,
            new WarehouseWorkAssignmentInput(scenario.Actor.Id, null, StableCode("WORK", request.Seed, "pick-assign")),
            scenario.Actor.Id,
            cancellationToken), "pick-work:assign");
        await VerifyMutationAsync("pick-work-assigned");
        Require(await workService.StartAsync(
            pickWork.Id,
            new WarehouseWorkCommandInput(StableCode("WORK", request.Seed, "pick-start")),
            scenario.Actor.Id,
            cancellationToken), "pick-work:start");
        await VerifyMutationAsync("pick-work-started");
        Require(await workService.CompleteAsync(
            pickWork.Id,
            new WarehouseWorkCompletionInput(
                StableCode("WORK", request.Seed, "pick-complete"),
                CompletionReference: salesOrder.DocumentNumber,
                Scans:
                [
                    new WarehouseWorkScanInput(
                        pickLine.Id,
                        pickLine.ItemId,
                        pickLine.SourceLocationId!.Value,
                        scenario.StagingLocation.Id,
                        3m,
                        pickLine.LicensePlateId,
                        LotId: pickLine.LotId,
                        SerialNumberId: pickLine.SerialNumberId,
                        SerialNumber: pickLine.SerialNumber)
                ]),
            scenario.Actor.Id,
            cancellationToken), "pick-work:complete");
        await VerifyMutationAsync("pick-work-completed");
        outcomes.Add("sales-order=confirmed,allocated,picked");

        var targetPlate = Require(await services.GetRequiredService<ILicensePlateService>().CreateAsync(
            new LicensePlateCreateInput(
                scenario.PrimaryWarehouse.Id,
                scenario.PackingLocation.Id,
                StableCode("LPN", request.Seed, "outbound-plate"),
                IsSscc: false,
                LicensePlateType.Tote,
                SourceReference: salesOrder.DocumentNumber),
            scenario.Actor.Id,
            cancellationToken), "license-plate:outbound-create");
        await VerifyMutationAsync("outbound-license-plate-created");
        var packingService = services.GetRequiredService<IPackingService>();
        var station = Require(await packingService.CreateStationAsync(
            new PackingStationInput(
                StableCode("PACK", request.Seed, "station"),
                request.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "محطة التعبئة" : "Seed packing station",
                scenario.PrimaryWarehouse.Id,
                scenario.PackingLocation.Id),
            scenario.Actor.Id,
            cancellationToken), "packing:station-create");
        await VerifyMutationAsync("packing-station-created");
        var session = Require(await packingService.StartSessionAsync(
            new PackingSessionStartInput(
                StableCode("SESSION", request.Seed, "packing-session"),
                scenario.PrimaryWarehouse.Id,
                station.Id,
                PackingSourceType.SalesOrder,
                salesOrder.DocumentNumber,
                salesOrder.Id,
                IdempotencyKey: StableCode("PACK", request.Seed, "session")),
            scenario.Actor.Id,
            cancellationToken), "packing:session-start");
        await VerifyMutationAsync("packing-session-started");
        var package = Require(await packingService.CreatePackageAsync(
            new PackingPackageCreateInput(
                session.Id,
                StableCode("PKG", request.Seed, "package"),
                targetPlate.Id,
                PackingPackageType.Carton,
                salesOrder.Id,
                IdempotencyKey: StableCode("PACK", request.Seed, "package-create")),
            scenario.Actor.Id,
            cancellationToken), "packing:package-create");
        await VerifyMutationAsync("packing-package-created");
        Require(await packingService.PackAsync(
            new PackingScanInput(
                package.Id,
                salesOrder.Lines.Single().Id,
                scenario.Items[0].Id,
                scenario.StagingLocation.Id,
                3m,
                InventoryStatusSystemIds.Available,
                pickLine.LicensePlateId,
                lotId,
                IdempotencyKey: StableCode("PACK", request.Seed, "pack-scan")),
            scenario.Actor.Id,
            cancellationToken), "packing:scan");
        await VerifyMutationAsync("packing-scan-completed");
        package = Require(await packingService.ClosePackageAsync(
            new PackingPackageMeasureInput(package.Id, StableCode("PACK", request.Seed, "package-close")),
            scenario.Actor.Id,
            cancellationToken), "packing:package-close");
        await VerifyMutationAsync("packing-package-closed");
        Require(await packingService.CompleteSessionAsync(
            session.Id,
            StableCode("PACK", request.Seed, "session-complete"),
            scenario.Actor.Id,
            cancellationToken), "packing:session-complete");
        await VerifyMutationAsync("packing-session-completed");

        var shipmentService = services.GetRequiredService<IShipmentService>();
        var carrier = Require(await shipmentService.CreateCarrierAsync(
            new CarrierInput(StableCode("CAR", request.Seed, "carrier"), "Seed carrier"),
            scenario.Actor.Id,
            cancellationToken), "shipping:carrier-create");
        var carrierService = Require(await shipmentService.CreateCarrierServiceAsync(
            new CarrierServiceInput(carrier.Id, "GROUND", "Ground"),
            scenario.Actor.Id,
            cancellationToken), "shipping:carrier-service-create");
        var shipment = Require(await shipmentService.CreateShipmentAsync(
            new ShipmentCreateInput(
                StableCode("SHIP", request.Seed, "shipment"),
                scenario.PrimaryWarehouse.Id,
                [package.Id],
                carrier.Id,
                carrierService.Id,
                IdempotencyKey: StableCode("SHIP", request.Seed, "shipment-create")),
            scenario.Actor.Id,
            cancellationToken), "shipping:shipment-create");
        await VerifyMutationAsync("shipment-created");
        var shipmentLoad = Require(await shipmentService.OpenLoadAsync(
            new ShipmentLoadInput(shipment.Id, scenario.DockLocation.Id, "SEED-TRAILER", "SEED-ROUTE",
                StableCode("SHIP", request.Seed, "shipment-load")),
            scenario.Actor.Id,
            cancellationToken), "shipping:load-open");
        await VerifyMutationAsync("shipment-load-opened");
        Require(await shipmentService.LoadPackageAsync(
            new ShipmentPackageScanInput(
                shipment.Id,
                package.Id,
                shipmentLoad.Loads.Single().Id,
                StableCode("SHIP", request.Seed, "shipment-load-package")),
            scenario.Actor.Id,
            cancellationToken), "shipping:load-package");
        await VerifyMutationAsync("shipment-package-loaded");
        shipment = Require(await shipmentService.ConfirmShipmentAsync(
            new ShipmentCommandInput(shipment.Id, StableCode("SHIP", request.Seed, "shipment-confirm")),
            scenario.Actor.Id,
            cancellationToken), "shipping:confirm");
        await VerifyMutationAsync("shipment-confirmed");
        var transactionsAfterShipment = await context.InventoryTransactions.CountAsync(cancellationToken);
        shipment = Require(await shipmentService.ConfirmShipmentAsync(
            new ShipmentCommandInput(shipment.Id, StableCode("SHIP", request.Seed, "shipment-confirm")),
            scenario.Actor.Id,
            cancellationToken), "shipping:confirm-replay");
        if (await context.InventoryTransactions.CountAsync(cancellationToken) != transactionsAfterShipment)
        {
            throw new InvalidOperationException("Replaying shipment confirmation created duplicate inventory transactions.");
        }
        await VerifyMutationAsync("shipment-confirmation-replayed-once");
        outcomes.Add("shipment-confirmation-replay=no-duplicate-transactions");
        outcomes.Add("shipment=packed,loaded,shipped");

        var shipmentLine = shipment.Lines.Single();
        var returnService = services.GetRequiredService<IReturnService>();
        var authorization = Require(await returnService.CreateAsync(
            new ReturnAuthorizationInput(
                StableCode("RMA", request.Seed, "return"),
                scenario.PrimaryWarehouse.Id,
                [new ReturnLineInput(
                    scenario.Items[0].Id,
                    1m,
                    salesOrder.Lines.Single().Id,
                    shipmentLine.Id,
                    lotId)],
                scenario.Customers[0].Id,
                salesOrder.Id,
                shipment.Id,
                package.Id,
                scenario.ReturnsLocation.Id,
                Reason: "Seeded customer return",
                IdempotencyKey: StableCode("RMA", request.Seed, "return-create")),
            scenario.Actor.Id,
            cancellationToken), "return:create");
        await VerifyMutationAsync("customer-return-created");
        authorization = Require(await returnService.AuthorizeAsync(
            new ReturnCommandInput(authorization.Id, StableCode("RMA", request.Seed, "return-authorize")),
            scenario.Actor.Id,
            cancellationToken), "return:authorize");
        await VerifyMutationAsync("customer-return-authorized");
        var returnLine = authorization.Lines.Single();
        var receivedReturn = Require(await returnService.ReceiveAsync(
            new ReturnReceiptInput(
                authorization.Id,
                returnLine.Id,
                1m,
                InventoryStatusSystemIds.ReturnPending,
                LotId: lotId,
                IdempotencyKey: StableCode("RMA", request.Seed, "return-receive")),
            scenario.Actor.Id,
            cancellationToken), "return:receive");
        await VerifyMutationAsync("customer-return-received");
        receivedReturn = Require(await returnService.InspectAsync(
            new ReturnCommandInput(authorization.Id, StableCode("RMA", request.Seed, "return-inspect")),
            scenario.Actor.Id,
            cancellationToken), "return:inspect");
        await VerifyMutationAsync("customer-return-inspected");
        var returnReceipt = receivedReturn.Receipts.Single();
        receivedReturn = Require(await returnService.DisposeAsync(
            new ReturnDispositionInput(
                authorization.Id,
                returnReceipt.Id,
                ReturnDispositionKind.RestockAvailable,
                1m,
                scenario.StorageLocation.Id,
                InventoryStatusSystemIds.Available,
                "Seed inspection passed",
                StableCode("RMA", request.Seed, "return-dispose")),
            scenario.Actor.Id,
            cancellationToken), "return:restock");
        await VerifyMutationAsync("customer-return-disposed");
        Require(await returnService.CloseAsync(
            new ReturnCommandInput(authorization.Id, StableCode("RMA", request.Seed, "return-close")),
            scenario.Actor.Id,
            cancellationToken), "return:close");
        await VerifyMutationAsync("customer-return-closed");
        outcomes.Add("return=authorized,received,inspected,restocked,closed");

        var countService = services.GetRequiredService<ICycleCountService>();
        var countPlan = Require(await countService.SavePlanAsync(
            null,
            new CycleCountPlanInput(
                StableCode("COUNT", request.Seed, "cycle-count-plan"),
                scenario.PrimaryWarehouse.Id,
                scenario.StorageLocation.Id,
                scenario.Items[0].Id,
                null,
                1,
                0m,
                Blind: false,
                CycleCountFreezePolicy.SnapshotAndReconcile,
                SeededAt.UtcDateTime.AddDays(-1)),
            scenario.Actor.Id,
            cancellationToken), "cycle-count:plan-create");
        await VerifyMutationAsync("cycle-count-plan-created");
        var countRun = Require(await countService.GenerateAsync(
            new CycleCountGenerationQuery(scenario.PrimaryWarehouse.Id, countPlan.Id),
            scenario.Actor.Id,
            cancellationToken), "cycle-count:generate");
        await VerifyMutationAsync("cycle-count-task-generated");
        if (countRun.TasksCreated != 1 ||
            countRun.LinesCreated < 1 ||
            countRun.Tasks.Count != 1 ||
            countRun.Tasks[0].LineCount != countRun.LinesCreated)
        {
            throw new InvalidOperationException(
                $"The seeded cycle-count plan produced {countRun.TasksCreated} tasks and {countRun.LinesCreated} lines.");
        }
        outcomes.Add("cycle-count=plan-created,task-generated");

        await FillInventoryRowsAsync(
            context,
            services,
            plan,
            scenario,
            cancellationToken);
        outcomes.Add($"inventory-rows=command-generated:{plan.EstimatedCounts["inventoryRows"]}");

        var queriedSalesOrder = Require(await salesOrderService.GetAsync(salesOrder.Id, cancellationToken), "sales-order:read");
        if (receipt.Lines.Single().PurchaseOrderId != purchaseOrder.Id ||
            queriedSalesOrder.Lines.Single().ShippedBaseQuantity != 3m)
        {
            throw new InvalidOperationException("Generated document totals failed normal-service readback.");
        }

        var reconciliation = Require(await services.GetRequiredService<IInventoryReconciliationService>().ReconcileAsync(
            new InventoryReconciliationQuery(WarehouseId: scenario.PrimaryWarehouse.Id, Deep: true),
            cancellationToken), "inventory:reconcile");
        var actualCounts = await CountActualRowsAsync(context, cancellationToken);
        if (actualCounts["warehouses"] != plan.EstimatedCounts["warehouses"] ||
            actualCounts["locations"] != plan.EstimatedCounts["locations"] ||
            actualCounts["items"] != plan.EstimatedCounts["items"] ||
            actualCounts["suppliers"] != plan.EstimatedCounts["suppliers"] ||
            actualCounts["customers"] != plan.EstimatedCounts["customers"] ||
            actualCounts["inventoryRows"] != plan.EstimatedCounts["inventoryRows"])
        {
            throw new InvalidOperationException("Generated catalog or inventory row counts do not match the bounded profile plan.");
        }

        var logicalFingerprint = await FingerprintLogicalDatasetAsync(context, cancellationToken);
        stopwatch.Stop();
        var report = new DataGenerationRunReport(
            plan.GeneratorVersion,
            plan.Profile,
            plan.SeedFingerprint,
            target.TargetIdentifier,
            stopwatch.ElapsedMilliseconds,
            actualCounts,
            outcomes,
            logicalFingerprint,
            reconciliation.IsClean,
            reconciliation.IssueCount,
            reconciliation.TransactionsScanned);
        AppendEvidence(report);
        return new DataGenerationFixtureResult(
            report,
            new DataGenerationActorCredentials(
                scenario.Actor.Id,
                scenario.Actor.UserName!,
                scenario.ActorPassword));
    }

    private static void AppendEvidence(DataGenerationRunReport report)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (EvidenceGate)
        {
            var existingReports = File.Exists(path)
                ? JsonSerializer.Deserialize<DataGenerationEvidenceEnvelope>(
                    File.ReadAllText(path),
                    EvidenceSerializerOptions)?.Reports ?? []
                : [];
            var json = JsonSerializer.Serialize(
                new DataGenerationEvidenceEnvelope(existingReports.Append(report).ToArray()),
                EvidenceSerializerOptions);
            File.WriteAllText(path, json);
        }
    }

    private sealed record DataGenerationEvidenceEnvelope(
        [property: JsonPropertyName("reports")] IReadOnlyList<DataGenerationRunReport> Reports);

    private static ServiceProvider BuildServiceProvider(
        string connectionString,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .ClearProviders()
            .AddProvider(new DiagnosticLoggerProvider())
            .SetMinimumLevel(LogLevel.Error));
        services.AddSingleton<Wms.Application.Context.IClock>(new FixedClock(SeededAt));
        services.AddWmsInfrastructure(connectionString, WmsDatabaseProvider.PostgreSql);
        services.AddWmsDesktopIdentity();
        services.AddWmsApplication();
        configureServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    internal static ServiceProvider CreateServiceProviderForExistingTarget(
        PostgreSqlTestDatabase target,
        Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ValidateIsolatedTarget(target);
        return BuildServiceProvider(target.ScopedConnectionString!, configureServices);
    }

    private static void ValidateIsolatedTarget(PostgreSqlTestDatabase target)
    {
        var schema = target.TargetIdentifier;
        if (!target.IsAvailable ||
            !schema.StartsWith("wms_test_", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(target.ScopedConnectionString))
        {
            throw new InvalidOperationException("The writer accepts only a fresh PostgreSQL test schema created by the isolated test fixture.");
        }

        var connection = new NpgsqlConnectionStringBuilder(target.ScopedConnectionString);
        if (!string.Equals(connection.SearchPath, schema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The isolated target schema does not match its PostgreSQL search path.");
        }
    }

    private static async Task EnsureFreshTargetAsync(
        WmsDbContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The deterministic fixture requires PostgreSQL.");
        }

        var occupied = await context.Warehouses.AnyAsync(cancellationToken) ||
                       await context.Items.AnyAsync(cancellationToken) ||
                       await context.Suppliers.AnyAsync(cancellationToken) ||
                       await context.Customers.AnyAsync(cancellationToken) ||
                       await context.Stock.AnyAsync(cancellationToken) ||
                       await context.Movements.AnyAsync(cancellationToken) ||
                       await context.PurchaseOrders.AnyAsync(cancellationToken) ||
                       await context.Receipts.AnyAsync(cancellationToken) ||
                       await context.SalesOrders.AnyAsync(cancellationToken) ||
                       await context.WarehouseWorks.AnyAsync(cancellationToken) ||
                       await context.Shipments.AnyAsync(cancellationToken) ||
                       await context.ReturnAuthorizations.AnyAsync(cancellationToken) ||
                       await context.Users.AnyAsync(cancellationToken);
        if (occupied)
        {
            throw new InvalidOperationException("The data-generation target is not empty. Existing schemas are never reset or reused.");
        }
    }

    private static async Task<SeedScenario> CreateReferenceScenarioAsync(
        WmsDbContext context,
        IServiceProvider services,
        DataGenerationPlan plan,
        CancellationToken cancellationToken)
    {
        var profile = WmsDataGenerationProfiles.All.Single(value => value.Name == plan.Profile);
        var names = IsArabic(plan.Locale);
        var warehouses = Enumerable.Range(0, plan.EstimatedCounts["warehouses"])
            .Select(index => new Warehouse(
                StableCode("WH", plan.SeedFingerprint, $"warehouse:{index}", 20),
                names ? $"مخزن {index + 1}" : $"Warehouse {index + 1}",
                names ? $"مخزن {index + 1}" : $"Warehouse {index + 1}"))
            .ToArray();
        context.Warehouses.AddRange(warehouses);
        context.UnitOfMeasures.Add(new UnitOfMeasure(
            "EA",
            UnitOfMeasureCategory.Count,
            0,
            "ea",
            "Each",
            "قطعة"));
        await context.SaveChangesAsync(cancellationToken);

        var locationGroups = new Dictionary<int, Location[]>();
        foreach (var warehouse in warehouses)
        {
            var locations = Enumerable.Range(0, profile.LocationsPerWarehouse * plan.EstimatedCounts["warehouses"] / warehouses.Length)
                .Select(index => CreateLocation(warehouse, plan, index, names))
                .ToArray();
            locationGroups.Add(warehouse.Id, locations);
            context.Locations.AddRange(locations);
            context.WarehouseNumberSequences.Add(new WarehouseNumberSequence(warehouse.Id));
        }
        await context.SaveChangesAsync(cancellationToken);

        var items = Enumerable.Range(0, plan.EstimatedCounts["items"])
            .Select(index =>
            {
                var item = new Item(
                    StableCode("SKU", plan.SeedFingerprint, $"item:{index}", 50),
                    names ? $"صنف تجريبي {index + 1}" : $"Generated item {index + 1}",
                    "EA",
                    requiresLot: index == 0,
                    requiresSerial: index == 1 && profile.IncludeEdgeCases);
                item.UpdateMasterData(new ItemMasterDetails(
                    item.Name,
                    LocalizedName: names ? item.Name : $"صنف تجريبي {index + 1}",
                    PurchaseUnit: "EA",
                    SalesUnit: "EA",
                    RequiresLot: index == 0,
                    RequiresExpiry: index == 0,
                    UseFefo: index == 0,
                    ShelfLifeDays: index == 0 ? 365 : 0,
                    RequiresSerial: index == 1 && profile.IncludeEdgeCases,
                    NetWeightKg: 1m,
                    SalesPrice: 10m,
                    StandardCost: 5m));
                return item;
            })
            .ToArray();
        var suppliers = Enumerable.Range(0, plan.EstimatedCounts["suppliers"])
            .Select(index => new Supplier(
                StableCode("SUP", plan.SeedFingerprint, $"supplier:{index}", 50),
                names ? $"مورد {index + 1}" : $"Generated supplier {index + 1}",
                defaultCurrencyCode: "USD"))
            .ToArray();
        var customers = Enumerable.Range(0, plan.EstimatedCounts["customers"])
            .Select(index => new Customer(
                StableCode("CUST", plan.SeedFingerprint, $"customer:{index}", 50),
                names ? $"عميل {index + 1}" : $"Generated customer {index + 1}",
                localizedName: names ? null : $"عميل {index + 1}",
                allowPartialShipment: index == 0))
            .ToArray();
        context.AddRange(items);
        context.AddRange(suppliers);
        context.AddRange(customers);
        await context.SaveChangesAsync(cancellationToken);

        var actorName = StableCode("seed", plan.SeedFingerprint, "actor", 40).ToLowerInvariant();
        var password = $"A1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}z";
        var actor = new WmsUser
        {
            UserName = actorName,
            Email = $"{actorName}@example.test",
            EmailConfirmed = true,
            DisplayName = names ? "مشغل الاختبار" : "Generated test operator",
            EmployeeCode = StableCode("EMP", plan.SeedFingerprint, "actor", 40),
            Locale = plan.Locale,
            TimeZone = "UTC",
            IsActive = true,
            LockoutEnabled = true
        };
        var userManager = services.GetRequiredService<UserManager<WmsUser>>();
        var createUser = await userManager.CreateAsync(actor, password);
        if (!createUser.Succeeded)
        {
            throw new InvalidOperationException("The generated test actor could not be provisioned through Identity.");
        }

        await services.GetRequiredService<WmsAuthorizationBootstrapper>().EnsureRolesAndPermissionsAsync(cancellationToken);
        var roleResult = await userManager.AddToRoleAsync(actor, WmsRoleNames.WarehouseManager);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException("The generated test actor could not be assigned the warehouse-manager role.");
        }

        foreach (var warehouse in warehouses)
        {
            context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
            {
                UserId = actor.Id,
                WarehouseId = warehouse.Id,
                IsDefault = warehouse.Id == warehouses[0].Id,
                AssignedAtUtc = SeededAt
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        services.GetRequiredService<DesktopUserSession>().SignIn(actor);

        return new SeedScenario(
            warehouses[0],
            locationGroups[warehouses[0].Id],
            items,
            suppliers,
            customers,
            actor,
            password,
            Locations: warehouses.SelectMany(warehouse => locationGroups[warehouse.Id]).ToArray());
    }

    private static Location CreateLocation(Warehouse warehouse, DataGenerationPlan plan, int index, bool arabic)
    {
        var kind = index switch
        {
            0 => (LocationType.Storage, true, true),
            1 => (LocationType.Staging, false, false),
            2 => (LocationType.Packing, false, false),
            3 => (LocationType.Returns, false, true),
            4 => (LocationType.Dock, false, false),
            5 => (LocationType.Shipping, false, false),
            6 => (LocationType.Receiving, false, true),
            7 => (LocationType.Quarantine, false, false),
            8 => (LocationType.Damaged, false, false),
            _ => (LocationType.Storage, true, true)
        };
        var (type, pickable, receivable) = kind;
        return new Location(
            StableCode("LOC", plan.SeedFingerprint, $"location:{warehouse.Code}:{index}", 50),
            arabic ? $"موقع {index + 1}" : $"Location {index + 1}",
            warehouse.Id,
            type: type,
            isPickable: pickable,
            isReceivable: receivable,
            isCountable: type is LocationType.Storage or LocationType.Returns or LocationType.Quarantine or LocationType.Damaged);
    }

    private static async Task AddEdgeCaseDimensionsAsync(
        WmsDbContext context,
        IServiceProvider services,
        DataGenerationPlan plan,
        SeedScenario scenario,
        string seed,
        List<string> outcomes,
        Func<string, Task> verifyMutationAsync,
        CancellationToken cancellationToken)
    {
        var profile = WmsDataGenerationProfiles.All.Single(value => value.Name == plan.Profile);
        if (!profile.IncludeEdgeCases || scenario.Items.Length < 3)
        {
            return;
        }

        var serialReceipt = Require(await services.GetRequiredService<IReceiveItemUseCase>().ExecuteAsync(
            new ReceiveItemDto(
                scenario.Items[1].Sku,
                scenario.ReceivingLocation.Code,
                1m,
                SerialNumber: StableCode("SN", seed, "serial", 50),
                ReferenceNumber: StableCode("EDGE", seed, "serial-receipt"),
                UnitOfMeasure: "EA"),
            scenario.Actor.Id,
            StableCode("IDEM", seed, "serial-receipt"),
            cancellationToken), "edge:serial-receipt");
        await verifyMutationAsync("serial-receipt-created");

        var owner = new InventoryOwner(
            StableCode("OWN", seed, "external-owner", 80),
            InventoryOwnerKind.ExternalOwner,
            IsArabic(plan.Locale) ? "مالك خارجي" : "Generated external owner",
            externalOwnerReference: StableCode("EXT", seed, "external-reference", 120));
        context.InventoryOwners.Add(owner);
        await context.SaveChangesAsync(cancellationToken);
        Require(await services.GetRequiredService<IReceiveItemUseCase>().ExecuteAsync(
            new ReceiveItemDto(
                scenario.Items[2].Sku,
                scenario.ReceivingLocation.Code,
                2m,
                ReferenceNumber: StableCode("EDGE", seed, "owner-receipt"),
                UnitOfMeasure: "EA",
                OwnerKind: InventoryOwnerKind.ExternalOwner,
                InventoryOwnerId: owner.Id,
                OwnerCodeSnapshot: owner.OwnerCode),
            scenario.Actor.Id,
            StableCode("IDEM", seed, "owner-receipt"),
            cancellationToken), "edge:owned-receipt");
        await verifyMutationAsync("external-owner-receipt-created");
        outcomes.Add("edge-dimensions=serial-and-external-owner-created-by-receipt-commands");

        var serial = await context.SerialNumbers.AsNoTracking()
            .SingleAsync(value => value.Number == StableCode("SN", seed, "serial", 50), cancellationToken);
        if (serial.ItemId != scenario.Items[1].Id || serial.CurrentLocationId != scenario.ReceivingLocation.Id ||
            serialReceipt.ItemSku != scenario.Items[1].Sku)
        {
            throw new InvalidOperationException("The generated serial number is not linked to its received item and location.");
        }
    }

    private static async Task FillInventoryRowsAsync(
        WmsDbContext context,
        IServiceProvider services,
        DataGenerationPlan plan,
        SeedScenario scenario,
        CancellationToken cancellationToken)
    {
        var desired = plan.EstimatedCounts["inventoryRows"];
        var existingRows = await context.Stock.AsNoTracking().CountAsync(cancellationToken);
        if (existingRows > desired)
        {
            throw new InvalidOperationException(
                $"The operational workflow created {existingRows} inventory rows, above the profile ceiling of {desired}.");
        }

        var occupied = await context.Stock.AsNoTracking()
            .Select(stock => new { stock.ItemId, stock.LocationId })
            .ToListAsync(cancellationToken);
        var occupiedPairs = occupied.Select(value => (value.ItemId, value.LocationId)).ToHashSet();
        var adjustments = new List<StockAdjustmentDto>(desired - existingRows);
        foreach (var item in scenario.Items.Where(value => !value.RequiresLot && !value.RequiresSerial))
        {
            foreach (var location in scenario.Locations.Where(value => value.IsActive && value.IsPickable))
            {
                if (existingRows + adjustments.Count >= desired)
                {
                    break;
                }
                if (!occupiedPairs.Add((item.Id, location.Id)))
                {
                    continue;
                }

                adjustments.Add(new StockAdjustmentDto(
                    item.Sku,
                    location.Code,
                    1m,
                    "deterministic profile opening balance",
                    UnitOfMeasure: "EA"));
            }

            if (existingRows + adjustments.Count >= desired)
            {
                break;
            }
        }

        if (existingRows + adjustments.Count != desired)
        {
            throw new InvalidOperationException(
                $"The profile requested {desired} inventory rows but the available unique item/location dimensions produced {existingRows + adjustments.Count}.");
        }

        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, adjustments.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(8, Math.Max(1, adjustments.Count)),
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var stockAdjustments = scope.ServiceProvider.GetRequiredService<IStockAdjustmentUseCase>();
                var result = await stockAdjustments.ExecuteAsync(
                    adjustments[index],
                    scenario.Actor.Id,
                    token);
                if (result.IsFailure)
                {
                    var diagnostics = DiagnosticLoggerProvider.Drain();
                    var suffix = diagnostics.Count == 0
                        ? string.Empty
                        : $"{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}";
                    throw new InvalidOperationException(
                        $"Data generation command 'inventory:opening-adjustment' failed: {result.ErrorCode} {result.Error}{suffix}");
                }
            });
    }

    private static async Task<IReadOnlyDictionary<string, int>> CountActualRowsAsync(
        WmsDbContext context,
        CancellationToken cancellationToken) => new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["warehouses"] = await context.Warehouses.CountAsync(cancellationToken),
            ["locations"] = await context.Locations.CountAsync(cancellationToken),
            ["items"] = await context.Items.CountAsync(cancellationToken),
            ["uoms"] = await context.UnitOfMeasures.CountAsync(cancellationToken),
            ["suppliers"] = await context.Suppliers.CountAsync(cancellationToken),
            ["customers"] = await context.Customers.CountAsync(cancellationToken),
            ["lots"] = await context.Lots.CountAsync(cancellationToken),
            ["serials"] = await context.SerialNumbers.CountAsync(cancellationToken),
            ["licensePlates"] = await context.LicensePlates.CountAsync(cancellationToken),
            ["owners"] = await context.InventoryOwners.CountAsync(cancellationToken),
            ["purchaseOrders"] = await context.PurchaseOrders.CountAsync(cancellationToken),
            ["receipts"] = await context.Receipts.CountAsync(cancellationToken),
            ["salesOrders"] = await context.SalesOrders.CountAsync(cancellationToken),
            ["warehouseWorks"] = await context.WarehouseWorks.CountAsync(cancellationToken),
            ["shipments"] = await context.Shipments.CountAsync(cancellationToken),
            ["returns"] = await context.ReturnAuthorizations.CountAsync(cancellationToken),
            ["cycleCountPlans"] = await context.CycleCountPlans.CountAsync(cancellationToken),
            ["cycleCountTasks"] = await context.CycleCountTasks.CountAsync(cancellationToken),
            ["cycleCountLines"] = await context.CycleCountLines.CountAsync(cancellationToken),
            ["inventoryRows"] = await context.Stock.CountAsync(cancellationToken),
            ["reservations"] = await context.InventoryReservations.CountAsync(cancellationToken),
            ["reservationAllocations"] = await context.InventoryReservationAllocations.CountAsync(cancellationToken),
            ["inventoryTransactions"] = await context.InventoryTransactions.CountAsync(cancellationToken),
            ["purchaseOrderReceiptAllocations"] = await context.PurchaseOrderReceiptAllocations.CountAsync(cancellationToken)
        };

    private static async Task<string> FingerprintLogicalDatasetAsync(
        WmsDbContext context,
        CancellationToken cancellationToken)
    {
        var warehouses = await context.Warehouses.AsNoTracking()
            .OrderBy(value => value.Code)
            .Select(value => new { value.Code, value.Name, value.ArabicName })
            .ToListAsync(cancellationToken);
        var items = await context.Items.AsNoTracking()
            .OrderBy(value => value.Sku)
            .Select(value => new { value.Sku, value.Name, value.LocalizedName, value.UnitOfMeasure, value.RequiresLot, value.RequiresSerial })
            .ToListAsync(cancellationToken);
        var balances = await context.InventoryBalances.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Include(value => value.Warehouse)
            .Include(value => value.Lot)
            .Include(value => value.LicensePlate)
            .Include(value => value.InventoryStatus)
            .OrderBy(value => value.Item.Sku)
            .ThenBy(value => value.Location.Code)
            .ThenBy(value => value.Lot == null ? string.Empty : value.Lot.Number)
            .ThenBy(value => value.SerialNumber)
            .ThenBy(value => value.LicensePlate == null ? string.Empty : value.LicensePlate.Number)
            .ThenBy(value => value.InventoryStatus.Code)
            .Select(value => new
            {
                Warehouse = value.Warehouse.Code,
                Item = value.Item.Sku,
                Location = value.Location.Code,
                Lot = value.Lot == null ? null : value.Lot.Number,
                value.SerialNumber,
                LicensePlate = value.LicensePlate == null ? null : value.LicensePlate.Number,
                Status = value.InventoryStatus.Code,
                value.OnHandQuantity,
                value.ReservedQuantity,
                value.OwnerKind,
                value.OwnerCodeSnapshot
            })
            .ToListAsync(cancellationToken);
        var documents = await context.PurchaseOrders.AsNoTracking()
            .Select(value => new { value.DocumentNumber, value.Status })
            .ToListAsync(cancellationToken);
        var payload = JsonSerializer.Serialize(new { warehouses, items, balances, documents });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string StableCode(string prefix, string seed, string purpose, int maxLength = 24)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}\n{purpose}")))
            .ToUpperInvariant();
        var suffixLength = Math.Min(16, maxLength - prefix.Length - 1);
        return $"{prefix}-{digest[..suffixLength]}";
    }

    private static bool IsArabic(string locale) =>
        locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase);

    private static T Require<T>(Wms.Application.Common.Result<T> result, string operation)
    {
        if (result.IsFailure)
        {
            var diagnostics = DiagnosticLoggerProvider.Drain();
            var suffix = diagnostics.Count == 0 ? string.Empty : $"{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}";
            throw new InvalidOperationException($"Data generation command '{operation}' failed: {result.ErrorCode} {result.Error}{suffix}");
        }

        return result.Value;
    }

    private static Location FindLocation(Location[] locations, LocationType type) =>
        locations.FirstOrDefault(location => location.Type == type)
        ?? throw new InvalidOperationException($"No {type} location was generated for the workflow warehouse.");

    private sealed record SeedScenario(
        Warehouse PrimaryWarehouse,
        Location[] PrimaryLocations,
        Item[] Items,
        Supplier[] Suppliers,
        Customer[] Customers,
        WmsUser Actor,
        string ActorPassword,
        Location[] Locations)
    {
        public Location StorageLocation => PrimaryLocations[0];
        public Location StagingLocation => FindLocation(PrimaryLocations, LocationType.Staging);
        public Location PackingLocation => FindLocation(PrimaryLocations, LocationType.Packing);
        public Location ReturnsLocation => FindLocation(PrimaryLocations, LocationType.Returns);
        public Location DockLocation => FindLocation(PrimaryLocations, LocationType.Dock);
        public Location ReceivingLocation => FindLocation(PrimaryLocations, LocationType.Receiving);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : Wms.Application.Context.IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class DiagnosticLoggerProvider : ILoggerProvider
    {
        private static readonly ConcurrentQueue<string> Errors = new();

        public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(categoryName);

        public void Dispose()
        {
        }

        public static void Clear()
        {
            while (Errors.TryDequeue(out _))
            {
            }
        }

        public static List<string> Drain()
        {
            var messages = new List<string>();
            while (Errors.TryDequeue(out var message))
            {
                messages.Add(message);
            }

            return messages;
        }

        private sealed class DiagnosticLogger(string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    Errors.Enqueue($"[{categoryName}] {formatter(state, exception)}{(exception is null ? string.Empty : $"{Environment.NewLine}{exception}")}");
                }
            }
        }
    }

    private sealed class ConnectionPoolCleanup(string connectionString) : IDisposable
    {
        public void Dispose()
        {
            using var connection = new NpgsqlConnection(connectionString);
            NpgsqlConnection.ClearPool(connection);
        }
    }
}
