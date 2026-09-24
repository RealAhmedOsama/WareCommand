using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.DataGeneration;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.SupplierReturns;
using Wms.Application.Transfers;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Tests.Integration;

public sealed record PostgreSqlJourneyCheckpointEvidence(
    string Name,
    int IssueCount,
    long TransactionsScanned,
    bool ExpectedClean = true);

public sealed record PostgreSqlJourneyEvidence(
    string Scenario,
    string TargetIdentifier,
    IReadOnlyList<string> OperationOutcomes,
    IReadOnlyList<string> UnsupportedOrSkippedScenarios,
    IReadOnlyList<PostgreSqlJourneyCheckpointEvidence> Checkpoints,
    IReadOnlyDictionary<string, decimal> ActualQuantities,
    IReadOnlyDictionary<string, int> ActualCounts,
    bool ReconciliationClean,
    int UnexpectedReconciliationIssueCount,
    int ExpectedReconciliationIssueCount);

public sealed partial class PostgreSqlJourneyTests
{
    private static readonly int[] TransferEntrySequences = [1, 2];
    private static readonly string[] ReplenishmentWorkPlanDecisions =
        ["work-created", "partial-source-work-created"];

    private static readonly JsonSerializerOptions JourneyEvidenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly object JourneyEvidenceGate = new();

    [PostgreSqlFact]
    public async Task CrossWarehouseTransferReconcilesEveryTransitionAndReplaysOnce()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seedCheckpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-130-transfer-journey",
                Environment: "Testing",
                Locale: "en-US"),
            checkpointAsync: (name, checkpointContext, checkpointReconciliation, cancellationToken) =>
                ReconcileAndRecordAsync(
                    name,
                    checkpointContext,
                    checkpointReconciliation,
                    seedCheckpoints,
                    cancellationToken));
        Assert.Equal(33, seedCheckpoints.Count);

        using var provider = DeterministicPostgreSqlDataGenerationFixture
            .CreateServiceProviderForExistingTarget(target);
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var userManager = services.GetRequiredService<UserManager<WmsUser>>();
        var actor = await userManager.FindByIdAsync(seeded.ActorCredentials.UserId);
        Assert.NotNull(actor);
        services.GetRequiredService<DesktopUserSession>().SignIn(actor);

        var context = services.GetRequiredService<WmsDbContext>();
        var reconciliation = services.GetRequiredService<IInventoryReconciliationService>();
        var transferService = services.GetRequiredService<ITransferService>();
        var generatedItem = await context.Items.AsNoTracking()
            .SingleAsync(value => value.RequiresLot);
        var generatedWarehouseId = await context.Warehouses.AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var generatedItemBalances = await context.InventoryBalances.AsNoTracking()
            .Include(value => value.Location)
            .Where(value => value.WarehouseId == generatedWarehouseId && value.ItemId == generatedItem.Id)
            .ToArrayAsync();
        var generatedItemOnHand = generatedItemBalances.Sum(value => value.OnHandQuantity);
        var generatedItemReserved = generatedItemBalances.Sum(value => value.ReservedQuantity);
        var generatedItemAvailable = generatedItemBalances.Sum(value => value.AvailableQuantity);
        var generatedStorageOnHand = generatedItemBalances
            .Where(value => value.Location.Type == LocationType.Storage)
            .Sum(value => value.OnHandQuantity);
        var generatedReceivingOnHand = generatedItemBalances
            .Where(value => value.Location.Type == LocationType.Receiving)
            .Sum(value => value.OnHandQuantity);
        Assert.Equal(18m, generatedItemOnHand);
        Assert.Equal(0m, generatedItemReserved);
        Assert.Equal(18m, generatedItemAvailable);
        Assert.Equal(18m, generatedStorageOnHand);
        Assert.Equal(0m, generatedReceivingOnHand);
        Assert.All(generatedItemBalances, value => Assert.True(value.LotId.HasValue));
        Assert.All(generatedItemBalances, value => Assert.Equal(InventoryOwnerKind.CompanyOwned, value.OwnerKind));
        var warehouses = await context.Warehouses.AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .Take(2)
            .ToArrayAsync();
        Assert.Equal(2, warehouses.Length);
        var sourceWarehouseId = warehouses[0];
        var destinationWarehouseId = warehouses[1];

        var sourceCandidates = await context.Stock.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Where(value => value.Location.WarehouseId == sourceWarehouseId &&
                            value.Location.IsPickable &&
                            value.Item.RequiresLot == false &&
                            value.Item.RequiresSerial == false &&
                            value.LotId == null &&
                            value.SerialNumberId == null &&
                            value.LicensePlateId == null &&
                            value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                            value.OwnerKind == InventoryOwnerKind.CompanyOwned)
            .OrderBy(value => value.ItemId)
            .ThenBy(value => value.LocationId)
            .ToArrayAsync();
        var sourceBalance = sourceCandidates.FirstOrDefault(value => value.QuantityAvailable.Value >= 1m);
        Assert.NotNull(sourceBalance);
        var destinationLocationId = await context.Locations.AsNoTracking()
            .Where(value => value.WarehouseId == destinationWarehouseId &&
                            value.Type == LocationType.Storage &&
                            value.IsReceivable)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var originalSourceLocationQuantity = await SumLocationQuantityAsync(
            context,
            sourceBalance.LocationId,
            sourceBalance.ItemId);
        var originalDestinationLocationQuantity = await SumLocationQuantityAsync(
            context,
            destinationLocationId,
            sourceBalance.ItemId);
        var transit = new Location(
            "J130-TRANSIT",
            "Issue 130 transfer transit",
            sourceWarehouseId,
            type: LocationType.Transit,
            isPickable: false,
            isReceivable: false,
            isCountable: false);
        context.Locations.Add(transit);
        await context.SaveChangesAsync();

        var originalItemQuantity = await SumItemQuantityAsync(context, sourceBalance.ItemId);
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var outcomes = new List<string>();

        async Task ReconcileCheckpointAsync(string checkpoint)
        {
            context.ChangeTracker.Clear();
            var report = await reconciliation.ReconcileAsync(
                new InventoryReconciliationQuery(Deep: true));
            Assert.True(report.IsSuccess, $"{checkpoint}: {report.FirstError?.Message}");
            Assert.True(
                report.Value.IsClean,
                $"{checkpoint}: {string.Join("; ", report.Value.Issues.Select(value => $"{value.Check}:{value.Code}:{value.Message}"))}");
            checkpoints.Add(new PostgreSqlJourneyCheckpointEvidence(
                checkpoint,
                report.Value.IssueCount,
                report.Value.TransactionsScanned,
                ExpectedClean: true));
        }

        var created = await transferService.CreateAsync(
            new TransferOrderInput(
                "J130-TRANSFER-001",
                "j130-transfer-create",
                sourceWarehouseId,
                destinationWarehouseId,
                transit.Id,
                [new TransferLineInput(
                    sourceBalance.ItemId,
                    1m,
                    "EA",
                    sourceBalance.LocationId,
                    destinationLocationId)]),
            actor.Id);
        Assert.True(created.IsSuccess, created.FirstError?.Message);
        var transferId = created.Value.Id;
        var lineId = created.Value.Lines.Single().Id;
        outcomes.Add($"created={created.Value.Status}");
        await ReconcileCheckpointAsync("transfer-created");

        var confirmed = await transferService.ConfirmAsync(
            new TransferCommandInput(transferId, "j130-transfer-confirm"),
            actor.Id);
        Assert.True(confirmed.IsSuccess, confirmed.FirstError?.Message);
        Assert.Equal(TransferOrderStatus.Confirmed, confirmed.Value.Status);
        outcomes.Add($"confirmed={confirmed.Value.Status}");
        await ReconcileCheckpointAsync("transfer-confirmed");

        var released = await transferService.ReleaseAsync(
            new TransferCommandInput(transferId, "j130-transfer-release"),
            actor.Id);
        Assert.True(released.IsSuccess, released.FirstError?.Message);
        Assert.Equal(TransferOrderStatus.Released, released.Value.Status);
        outcomes.Add($"released={released.Value.Status}");
        await ReconcileCheckpointAsync("transfer-released");

        var shipCommand = new TransferQuantityCommandInput(
            transferId,
            lineId,
            1m,
            "j130-transfer-ship");
        var shipped = await transferService.ShipAsync(shipCommand, actor.Id);
        Assert.True(shipped.IsSuccess, shipped.FirstError?.Message);
        Assert.Equal(TransferOrderStatus.InTransit, shipped.Value.Status);
        outcomes.Add($"shipped={shipped.Value.ShippedQuantity}");
        await ReconcileCheckpointAsync("transfer-shipped-to-transit");
        var transitAfterShip = await SumLocationQuantityAsync(context, transit.Id, sourceBalance.ItemId);
        Assert.Equal(
            originalSourceLocationQuantity - 1m,
            await SumLocationQuantityAsync(context, sourceBalance.LocationId, sourceBalance.ItemId));
        Assert.Equal(1m, transitAfterShip);
        Assert.Equal(originalItemQuantity, await SumItemQuantityAsync(context, sourceBalance.ItemId));
        var transitBalanceAfterShip = await context.InventoryBalances.AsNoTracking()
            .SingleAsync(value => value.LocationId == transit.Id && value.ItemId == sourceBalance.ItemId);
        Assert.Equal(1m, transitBalanceAfterShip.OnHandQuantity);
        Assert.Equal(0m, transitBalanceAfterShip.ReservedQuantity);
        Assert.Equal(InventoryStatusSystemIds.InTransit, transitBalanceAfterShip.InventoryStatusId);
        Assert.Equal(InventoryOwnerKind.CompanyOwned, transitBalanceAfterShip.OwnerKind);
        outcomes.Add("transit-balance=company-owned,in-transit,unreserved");

        var transactionCountAfterShip = await context.InventoryTransactions.CountAsync();
        var shipReplay = await transferService.ShipAsync(shipCommand, actor.Id);
        Assert.True(shipReplay.IsSuccess, shipReplay.FirstError?.Message);
        Assert.Equal(shipped.Value.ShippedQuantity, shipReplay.Value.ShippedQuantity);
        Assert.Equal(transactionCountAfterShip, await context.InventoryTransactions.CountAsync());
        outcomes.Add("ship-replay=no-duplicate-transactions");
        await ReconcileCheckpointAsync("transfer-ship-replayed-once");

        var receiveCommand = new TransferQuantityCommandInput(
            transferId,
            lineId,
            1m,
            "j130-transfer-receive");
        var received = await transferService.ReceiveAsync(receiveCommand, actor.Id);
        Assert.True(received.IsSuccess, received.FirstError?.Message);
        Assert.Equal(TransferOrderStatus.Received, received.Value.Status);
        outcomes.Add($"received={received.Value.ReceivedQuantity}");
        await ReconcileCheckpointAsync("transfer-received-at-destination");
        Assert.Equal(0m, await SumLocationQuantityAsync(context, transit.Id, sourceBalance.ItemId));
        Assert.Equal(
            originalDestinationLocationQuantity + 1m,
            await SumLocationQuantityAsync(context, destinationLocationId, sourceBalance.ItemId));
        Assert.Equal(originalItemQuantity, await SumItemQuantityAsync(context, sourceBalance.ItemId));

        var transactionCountAfterReceive = await context.InventoryTransactions.CountAsync();
        var receiveReplay = await transferService.ReceiveAsync(receiveCommand, actor.Id);
        Assert.True(receiveReplay.IsSuccess, receiveReplay.FirstError?.Message);
        Assert.Equal(received.Value.ReceivedQuantity, receiveReplay.Value.ReceivedQuantity);
        Assert.Equal(transactionCountAfterReceive, await context.InventoryTransactions.CountAsync());
        outcomes.Add("receive-replay=no-duplicate-transactions");
        await ReconcileCheckpointAsync("transfer-receive-replayed-once");

        var closed = await transferService.CloseAsync(
            new TransferCommandInput(transferId, "j130-transfer-close"),
            actor.Id);
        Assert.True(closed.IsSuccess, closed.FirstError?.Message);
        Assert.Equal(TransferOrderStatus.Closed, closed.Value.Status);
        outcomes.Add($"closed={closed.Value.Status}");
        await ReconcileCheckpointAsync("transfer-closed");

        var transferLedger = await context.InventoryTransactions.AsNoTracking()
            .Where(value => value.ReferenceType == "TransferOrder" &&
                            value.ReferenceId == created.Value.TransferNumber)
            .OrderBy(value => value.TransactionGroupId)
            .ThenBy(value => value.EntrySequence)
            .ToArrayAsync();
        Assert.Equal(4, transferLedger.Length);
        Assert.Equal(2, transferLedger.Select(value => value.TransactionGroupId).Distinct().Count());
        Assert.Equal(4, transferLedger.Select(value => value.IdempotencyKey).Distinct().Count());
        Assert.All(transferLedger, value =>
        {
            Assert.Equal(actor.Id, value.ActorUserId);
            Assert.Equal("TransferOrder", value.ReferenceType);
            Assert.Equal(created.Value.TransferNumber, value.ReferenceId);
            Assert.Equal(lineId, value.ReferenceLine);
        });
        foreach (var group in transferLedger.GroupBy(value => value.TransactionGroupId))
        {
            Assert.Equal(TransferEntrySequences, group.Select(value => value.EntrySequence).Order().ToArray());
        }
        outcomes.Add("transfer-ledger=4-legs,two-groups,authenticated-actor,source-reference");

        var corruptionTarget = await context.InventoryBalances.SingleAsync(value =>
            value.LocationId == destinationLocationId && value.ItemId == sourceBalance.ItemId);
        var corruptionTargetId = corruptionTarget.Id;
        var originalCorruptionTargetQuantity = corruptionTarget.OnHandQuantity;
        corruptionTarget.Apply(1m, 0m, allowNegativeStock: false);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        try
        {
            var corruptedReport = await reconciliation.ReconcileAsync(
                new InventoryReconciliationQuery(Deep: true));
            Assert.True(corruptedReport.IsSuccess, corruptedReport.FirstError?.Message);
            Assert.False(corruptedReport.Value.IsClean);
            Assert.True(corruptedReport.Value.IssueCount > 0);
            checkpoints.Add(new PostgreSqlJourneyCheckpointEvidence(
                "deliberate-balance-corruption-detected",
                corruptedReport.Value.IssueCount,
                corruptedReport.Value.TransactionsScanned,
                ExpectedClean: false));
        }
        finally
        {
            context.ChangeTracker.Clear();
            var corruptedBalance = await context.InventoryBalances.SingleAsync(value => value.Id == corruptionTargetId);
            corruptedBalance.Apply(-1m, 0m, allowNegativeStock: false);
            await context.SaveChangesAsync();
        }

        await ReconcileCheckpointAsync("transfer-corruption-rolled-back");
        var supplierReturnJourney = await RunSupplierReturnJourneyAsync(
            services,
            context,
            reconciliation,
            actor.Id,
            sourceWarehouseId);
        var replenishmentJourney = await RunReplenishmentJourneyAsync(
            services,
            context,
            reconciliation,
            actor.Id,
            sourceWarehouseId);
        Assert.Equal(10, checkpoints.Count);
        var finalItemQuantity = await SumItemQuantityAsync(context, sourceBalance.ItemId);
        var finalSourceLocationQuantity = await SumLocationQuantityAsync(
            context,
            sourceBalance.LocationId,
            sourceBalance.ItemId);
        var finalTransitQuantity = await SumLocationQuantityAsync(context, transit.Id, sourceBalance.ItemId);
        var finalDestinationLocationQuantity = await SumLocationQuantityAsync(
            context,
            destinationLocationId,
            sourceBalance.ItemId);
        Assert.Equal(originalItemQuantity, finalItemQuantity);
        Assert.True(checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)));
        var inboundQualityPutawayJourney = await RunInboundQualityPutawayJourneyAsync(
            services,
            context,
            reconciliation,
            actor.Id,
            sourceWarehouseId,
            target.TargetIdentifier);
        var unsupportedOrSkippedScenarios = new[]
        {
            "positive and zero-variance cycle-count physical-count journeys",
            "wave, cluster, cross-dock, kitting, and other disposition journeys",
            "partial quantities, shortages, cancellation, hold/release, and stale concurrency tokens",
            "authenticated HTTP boundary for these service journeys"
        };
        var generatedLifecycle = new PostgreSqlJourneyEvidence(
            "deterministic-inbound-outbound-return-cycle-count",
            target.TargetIdentifier,
            seeded.Report.OperationOutcomes,
            unsupportedOrSkippedScenarios,
            seedCheckpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["lotTrackedItemOnHandAfterInboundOutboundAndReturn"] = generatedItemOnHand,
                ["lotTrackedItemReservedAfterShipment"] = generatedItemReserved,
                ["lotTrackedItemAvailableAfterReturn"] = generatedItemAvailable,
                ["storageOnHandAfterRestock"] = generatedStorageOnHand,
                ["receivingLocationOnHandAfterPutaway"] = generatedReceivingOnHand
            },
            seeded.Report.ActualCounts,
            ReconciliationClean: seedCheckpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: seedCheckpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: seedCheckpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount));
        var transferJourney = new PostgreSqlJourneyEvidence(
            "cross-warehouse-transfer",
            target.TargetIdentifier,
            outcomes,
            unsupportedOrSkippedScenarios,
            checkpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["itemQuantityBefore"] = originalItemQuantity,
                ["itemQuantityAfter"] = finalItemQuantity,
                ["sourceLocationBefore"] = originalSourceLocationQuantity,
                ["sourceLocationAfter"] = finalSourceLocationQuantity,
                ["transitAfterShip"] = transitAfterShip,
                ["transitReservedAfterShip"] = transitBalanceAfterShip.ReservedQuantity,
                ["transitAfterReceive"] = finalTransitQuantity,
                ["destinationLocationBefore"] = originalDestinationLocationQuantity,
                ["destinationLocationAfter"] = finalDestinationLocationQuantity
            },
            new Dictionary<string, int>(StringComparer.Ordinal),
            ReconciliationClean: checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: checkpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: checkpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount));
        WriteJourneyEvidence(
            generatedLifecycle,
            transferJourney,
            inboundQualityPutawayJourney,
            supplierReturnJourney,
            replenishmentJourney);
    }

    private static async Task<PostgreSqlJourneyEvidence> RunReplenishmentJourneyAsync(
        IServiceProvider services,
        WmsDbContext context,
        IInventoryReconciliationService reconciliation,
        string actorUserId,
        int warehouseId)
    {
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var outcomes = new List<string>();

        async Task ReconcileCheckpointAsync(string checkpoint)
        {
            await ReconcileAndRecordAsync(
                checkpoint,
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);
        }

        var sourceBalance = await context.InventoryBalances.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Where(value =>
                value.WarehouseId == warehouseId &&
                value.Location.Type == LocationType.Storage &&
                value.Location.IsActive &&
                value.Location.IsPickable &&
                value.Item.IsActive &&
                !value.Item.RequiresLot &&
                !value.Item.RequiresSerial &&
                value.LotId == null &&
                value.SerialNumberId == null &&
                value.LicensePlateId == null &&
                value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                value.OwnerKind == InventoryOwnerKind.CompanyOwned &&
                value.InventoryOwnerId == null &&
                value.OwnerCodeSnapshot == InventoryOwnershipDimension.CompanyOwnerCode &&
                value.OnHandQuantity - value.ReservedQuantity >= 1m)
            .OrderBy(value => value.ItemId)
            .ThenBy(value => value.LocationId)
            .FirstAsync();
        var destination = await context.Locations.AsNoTracking()
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Type == LocationType.Storage &&
                            value.Id != sourceBalance.LocationId &&
                            value.IsActive &&
                            value.IsPickable)
            .OrderBy(value => value.Id)
            .FirstAsync();
        var destinationQuantityBefore = await SumLocationQuantityAsync(
            context,
            destination.Id,
            sourceBalance.ItemId);
        var plannedQuantity = 1m;
        var targetQuantity = destinationQuantityBefore + plannedQuantity;
        var policyService = services.GetRequiredService<IInventoryReplenishmentPolicyService>();
        var generationService = services.GetRequiredService<IReplenishmentExecutionService>();
        var workService = services.GetRequiredService<IWarehouseWorkService>();
        var policy = await policyService.SaveAsync(
            null,
            new InventoryReplenishmentPolicyInput(
                sourceBalance.ItemId,
                warehouseId,
                destination.Id,
                MinimumQuantity: 0m,
                MaximumQuantity: targetQuantity + 100m,
                SafetyStockQuantity: 0m,
                ReorderPointQuantity: targetQuantity,
                TargetQuantity: targetQuantity,
                QuantityBasis: InventoryPolicyQuantityBasis.PhysicalAvailable,
                EffectiveFromUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            actorUserId);
        Assert.True(policy.IsSuccess, policy.FirstError?.Message);
        await ReconcileCheckpointAsync("replenishment-policy-saved");

        var totalQuantityBefore = await SumItemQuantityAsync(context, sourceBalance.ItemId);
        var generation = await generationService.GenerateAsync(
            new ReplenishmentGenerationQuery(warehouseId, policy.Value.Id),
            actorUserId);
        Assert.True(generation.IsSuccess, generation.FirstError?.Message);
        Assert.Equal(1, generation.Value.WorkCreated);
        Assert.Equal(0, generation.Value.Blocked);
        var plan = Assert.Single(generation.Value.Plans);
        Assert.Contains(plan.Decision, ReplenishmentWorkPlanDecisions);
        Assert.Equal(plannedQuantity, plan.PlannedQuantity);
        Assert.NotNull(plan.WorkId);
        Assert.Equal(destination.Id, plan.DestinationLocationId);
        Assert.NotEmpty(plan.Lines);
        Assert.All(plan.Lines, line => Assert.Equal(InventoryOwnerKind.CompanyOwned, line.OwnerKind));
        Assert.Equal(0m, await SumReservedQuantityAsync(context, sourceBalance.ItemId));
        await ReconcileCheckpointAsync("replenishment-work-created-without-stock-mutation");

        var replayedGeneration = await generationService.GenerateAsync(
            new ReplenishmentGenerationQuery(warehouseId, policy.Value.Id),
            actorUserId);
        Assert.True(replayedGeneration.IsSuccess, replayedGeneration.FirstError?.Message);
        Assert.Equal(0, replayedGeneration.Value.WorkCreated);
        Assert.Equal(1, replayedGeneration.Value.WorkReused);
        Assert.Equal(plan.WorkId, Assert.Single(replayedGeneration.Value.Plans).WorkId);
        outcomes.Add("policy-signal=generated-deterministic-work,replay-reused-open-work");
        await ReconcileCheckpointAsync("replenishment-open-work-reused");

        var workId = plan.WorkId!.Value;
        var work = await workService.GetAsync(workId);
        Assert.True(work.IsSuccess, work.FirstError?.Message);
        Assert.Equal(WarehouseWorkType.Replenishment, work.Value.Type);
        Assert.Equal(plan.Lines.Count, work.Value.Lines.Count);
        Assert.All(work.Value.Lines, line =>
        {
            var plannedLine = plan.Lines.Single(value => value.Sequence == line.Sequence);
            Assert.Equal(plannedLine.SourceLocationId, line.SourceLocationId);
            Assert.Equal(plannedLine.DestinationLocationId, line.DestinationLocationId);
            Assert.Equal(plannedLine.OwnerKind, line.OwnerKind);
            Assert.Equal(plannedLine.InventoryOwnerId, line.InventoryOwnerId);
            Assert.Equal(plannedLine.OwnerCodeSnapshot, line.OwnerCodeSnapshot);
        });
        var sourceQuantitiesBefore = new Dictionary<int, decimal>();
        foreach (var sourceLocationId in plan.Lines
                     .Select(value => value.SourceLocationId)
                     .Distinct())
        {
            sourceQuantitiesBefore[sourceLocationId] = await SumLocationQuantityAsync(
                context,
                sourceLocationId,
                sourceBalance.ItemId);
        }
        var assigned = await workService.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput(actorUserId, null, "j130-replenishment-assign"),
            actorUserId);
        Assert.True(assigned.IsSuccess, assigned.FirstError?.Message);
        await ReconcileCheckpointAsync("replenishment-work-assigned");
        var started = await workService.StartAsync(
            workId,
            new WarehouseWorkCommandInput("j130-replenishment-start"),
            actorUserId);
        Assert.True(started.IsSuccess, started.FirstError?.Message);
        await ReconcileCheckpointAsync("replenishment-work-started");

        var completionInput = new WarehouseWorkCompletionInput(
            "j130-replenishment-complete",
            CompletionReference: work.Value.WorkNumber,
            Scans: work.Value.Lines.Select(line => new WarehouseWorkScanInput(
                line.Id,
                line.ItemId,
                line.SourceLocationId!.Value,
                line.DestinationLocationId!.Value,
                line.PlannedQuantity,
                line.LicensePlateId,
                LotId: line.LotId,
                SerialNumberId: line.SerialNumberId,
                SerialNumber: line.SerialNumber)).ToArray());
        var completed = await workService.CompleteAsync(workId, completionInput, actorUserId);
        Assert.True(completed.IsSuccess, completed.FirstError?.Message);
        Assert.Equal(WarehouseWorkStatus.Completed, completed.Value.Status);
        Assert.Equal(
            totalQuantityBefore,
            await SumItemQuantityAsync(context, sourceBalance.ItemId));
        Assert.Equal(
            destinationQuantityBefore + plan.PlannedQuantity,
            await SumLocationQuantityAsync(context, destination.Id, sourceBalance.ItemId));
        foreach (var line in plan.Lines.GroupBy(value => value.SourceLocationId))
        {
            Assert.Equal(
                sourceQuantitiesBefore[line.Key] - line.Sum(value => value.PlannedQuantity),
                await SumLocationQuantityAsync(context, line.Key, sourceBalance.ItemId));
        }
        Assert.Equal(0m, await SumReservedQuantityAsync(context, sourceBalance.ItemId));
        outcomes.Add("scanner-completion=source-decrement,destination-increment,company-quantity-preserved");
        await ReconcileCheckpointAsync("replenishment-work-completed");

        var transactionsAfterCompletion = await context.InventoryTransactions.CountAsync();
        var completionReplay = await workService.CompleteAsync(workId, completionInput, actorUserId);
        Assert.True(completionReplay.IsSuccess, completionReplay.FirstError?.Message);
        Assert.Equal(WarehouseWorkStatus.Completed, completionReplay.Value.Status);
        Assert.Equal(transactionsAfterCompletion, await context.InventoryTransactions.CountAsync());
        outcomes.Add("completion-replay=no-duplicate-inventory-transactions");
        await ReconcileCheckpointAsync("replenishment-completion-replayed-once");

        var ledgerEntries = await context.InventoryTransactions.AsNoTracking()
            .Where(value => value.ReferenceId == work.Value.WorkNumber)
            .OrderBy(value => value.TransactionGroupId)
            .ThenBy(value => value.EntrySequence)
            .ToArrayAsync();
        Assert.Equal(plan.Lines.Count * 2, ledgerEntries.Length);
        Assert.All(ledgerEntries, value =>
        {
            Assert.Equal(InventoryTransactionType.Putaway, value.Type);
            Assert.Equal(actorUserId, value.ActorUserId);
            Assert.Equal("Movement", value.ReferenceType);
            Assert.Equal(InventoryOwnerKind.CompanyOwned, value.OwnerKind);
        });
        var movements = await context.Movements.AsNoTracking()
            .Where(value => value.ReferenceNumber == work.Value.WorkNumber)
            .ToArrayAsync();
        Assert.Equal(plan.Lines.Count, movements.Length);
        Assert.All(movements, value =>
        {
            Assert.Equal(MovementType.Putaway, value.Type);
            Assert.Equal(actorUserId, value.UserId);
            Assert.Equal(InventoryOwnerKind.CompanyOwned, value.OwnerKind);
        });
        var workCommands = await context.WarehouseWorkCommands.AsNoTracking()
            .Where(value => value.WarehouseWorkId == workId)
            .ToArrayAsync();
        Assert.All(workCommands, value => Assert.Equal(actorUserId, value.UserId));
        outcomes.Add("ledger-and-command-audit=actor-source-reference-owner-qualified");

        return new PostgreSqlJourneyEvidence(
            "replenishment-signal-work-completion",
            "issue-130-replenishment",
            outcomes,
            [
                "cycle-count execution and variance approval",
                "wave, cluster, cross-dock, kitting, and other disposition journeys",
                "partial quantities, shortages, cancellation, hold/release, and stale concurrency tokens",
                "authenticated HTTP boundary for these service journeys"
            ],
            checkpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["itemQuantityBefore"] = totalQuantityBefore,
                ["itemQuantityAfter"] = await SumItemQuantityAsync(context, sourceBalance.ItemId),
                ["destinationQuantityBefore"] = destinationQuantityBefore,
                ["destinationQuantityAfter"] = destinationQuantityBefore + plan.PlannedQuantity,
                ["replenishmentQuantity"] = plan.PlannedQuantity
            },
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["policyId"] = policy.Value.Id,
                ["workId"] = workId,
                ["workLines"] = plan.Lines.Count,
                ["workCommands"] = workCommands.Length,
                ["ledgerEntries"] = ledgerEntries.Length,
                ["movements"] = movements.Length
            },
            ReconciliationClean: checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: checkpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: checkpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount));
    }

    private static async Task<PostgreSqlJourneyEvidence> RunSupplierReturnJourneyAsync(
        IServiceProvider services,
        WmsDbContext context,
        IInventoryReconciliationService reconciliation,
        string actorUserId,
        int warehouseId)
    {
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var outcomes = new List<string>();

        async Task ReconcileCheckpointAsync(string checkpoint)
        {
            await ReconcileAndRecordAsync(
                checkpoint,
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);
        }

        var purchaseOrder = await context.PurchaseOrders.AsNoTracking()
            .Include(value => value.Lines)
            .SingleAsync(value => value.WarehouseId == warehouseId &&
                                  value.SourceType == "DATA-GENERATION");
        var receipt = await context.Receipts.AsNoTracking()
            .Include(value => value.Lines)
            .SingleAsync(value => value.PurchaseOrderId == purchaseOrder.Id);
        var receiptLine = receipt.Lines.Single();
        Assert.NotNull(receiptLine.PurchaseOrderLineId);
        var receiptMovement = await context.ReceiptLineMovements.AsNoTracking()
            .Where(value => value.ReceiptLineId == receiptLine.Id &&
                            value.Kind == ReceiptMovementKind.Receipt)
            .Select(value => value.Movement)
            .SingleAsync();
        var sourceStocks = await context.Stock.AsNoTracking()
            .Include(value => value.Location)
            .Where(value => value.ItemId == receiptLine.ItemId &&
                            value.Location.WarehouseId == warehouseId &&
                            value.Location.Type == LocationType.Storage &&
                            value.LotId == receiptMovement.LotId &&
                            value.SerialNumberId == receiptMovement.SerialNumberId &&
                            value.SerialNumber == receiptMovement.SerialNumber &&
                            value.LicensePlateId == receiptMovement.ToLicensePlateId &&
                            value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                            value.OwnerKind == receiptLine.OwnerKind &&
                            value.InventoryOwnerId == receiptLine.InventoryOwnerId &&
                            value.OwnerCodeSnapshot == receiptLine.OwnerCodeSnapshot)
            .OrderBy(value => value.LocationId)
            .ToArrayAsync();
        var sourceStock = sourceStocks.First(value => value.QuantityAvailable.Value > 0m);
        var stagingLocationId = await context.Locations.AsNoTracking()
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Type == LocationType.Staging &&
                            value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var supplierReturnService = services.GetRequiredService<ISupplierReturnService>();
        var workService = services.GetRequiredService<IWarehouseWorkService>();
        var sourceQuantity = sourceStock.QuantityAvailable.Value;
        var originalItemQuantity = await SumItemQuantityAsync(context, receiptLine.ItemId);
        var originalSourceQuantity = await SumLocationQuantityAsync(
            context,
            sourceStock.LocationId,
            receiptLine.ItemId);
        var originalStagingQuantity = await SumLocationQuantityAsync(
            context,
            stagingLocationId,
            receiptLine.ItemId);

        SupplierReturnCreateInput CreateInput(string returnNumber, decimal requestedQuantity) =>
            new(
                returnNumber,
                warehouseId,
                purchaseOrder.SupplierId,
                stagingLocationId,
                [new SupplierReturnLineInput(
                    receiptLine.ItemId,
                    requestedQuantity,
                    receiptLine.BaseUnitOfMeasure,
                    sourceStock.LocationId,
                    InventoryStatusSystemIds.Available,
                    "Supplier return from the received purchase-order lot",
                    receiptMovement.LotId,
                    receiptMovement.SerialNumberId,
                    receiptMovement.SerialNumber,
                    receiptMovement.ToLicensePlateId,
                    receiptLine.PurchaseOrderLineId,
                    ReceiptLineId: receiptLine.Id,
                    SourceReference: receipt.DocumentNumber)],
                "ISSUE-130-JOURNEY",
                "Supplier return journey qualification",
                purchaseOrder.Id,
                ReceiptId: receipt.Id);

        var overRequested = CreateInput("J130-SUPPLIER-SHORT", sourceQuantity + 1m);
        var rejected = await supplierReturnService.CreateAsync(overRequested, actorUserId);
        Assert.True(rejected.IsSuccess, rejected.FirstError?.Message);
        await ReconcileCheckpointAsync("supplier-return-shortage-document-created");
        var shortage = await supplierReturnService.ApproveAsync(
            new SupplierReturnCommandInput(rejected.Value.Id, "j130-supplier-shortage-approve"),
            actorUserId);
        Assert.True(shortage.IsFailure, "Approval must reject a return quantity above unreserved source stock.");
        Assert.Equal(0m, await context.Stock.AsNoTracking()
            .Where(value => value.Id == sourceStock.Id)
            .Select(value => value.QuantityReserved.Value)
            .SingleAsync());
        outcomes.Add("source-shortage=over-request-rejected-without-reservation");
        await ReconcileCheckpointAsync("supplier-return-shortage-rejected");

        var created = await supplierReturnService.CreateAsync(
            CreateInput("J130-SUPPLIER-001", sourceQuantity),
            actorUserId);
        Assert.True(created.IsSuccess, created.FirstError?.Message);
        await ReconcileCheckpointAsync("supplier-return-created");
        var approved = await supplierReturnService.ApproveAsync(
            new SupplierReturnCommandInput(created.Value.Id, "j130-supplier-approve"),
            actorUserId);
        Assert.True(approved.IsSuccess, approved.FirstError?.Message);
        Assert.Equal(SupplierReturnStatus.Approved, approved.Value.Status);
        await ReconcileCheckpointAsync("supplier-return-approved-and-reserved");

        var released = await supplierReturnService.ReleaseAsync(
            new SupplierReturnCommandInput(created.Value.Id, "j130-supplier-release"),
            actorUserId);
        Assert.True(released.IsSuccess, released.FirstError?.Message);
        Assert.Equal(SupplierReturnStatus.Picking, released.Value.Status);
        Assert.NotNull(released.Value.WarehouseWorkId);
        await ReconcileCheckpointAsync("supplier-return-released-to-work");

        var workId = released.Value.WarehouseWorkId!.Value;
        var work = await workService.GetAsync(workId);
        Assert.True(work.IsSuccess, work.FirstError?.Message);
        Assert.Single(work.Value.Lines);
        var assigned = await workService.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput(actorUserId, null, "j130-supplier-work-assign"),
            actorUserId);
        Assert.True(assigned.IsSuccess, assigned.FirstError?.Message);
        await ReconcileCheckpointAsync("supplier-return-work-assigned");
        var started = await workService.StartAsync(
            workId,
            new WarehouseWorkCommandInput("j130-supplier-work-start"),
            actorUserId);
        Assert.True(started.IsSuccess, started.FirstError?.Message);
        await ReconcileCheckpointAsync("supplier-return-work-started");

        var workLine = work.Value.Lines.Single();
        var completed = await workService.CompleteAsync(
            workId,
            new WarehouseWorkCompletionInput(
                "j130-supplier-work-complete",
                CompletionReference: created.Value.ReturnNumber,
                Scans:
                [
                    new WarehouseWorkScanInput(
                        workLine.Id,
                        workLine.ItemId,
                        workLine.SourceLocationId!.Value,
                        workLine.DestinationLocationId!.Value,
                        sourceQuantity,
                        workLine.LicensePlateId,
                        LotId: workLine.LotId,
                        SerialNumberId: workLine.SerialNumberId,
                        SerialNumber: workLine.SerialNumber)
                ]),
            actorUserId);
        Assert.True(completed.IsSuccess, completed.FirstError?.Message);
        Assert.Equal(WarehouseWorkStatus.Completed, completed.Value.Status);
        Assert.Equal(sourceQuantity, completed.Value.Lines.Single().ActualQuantity);
        Assert.Equal(
            originalSourceQuantity - sourceQuantity,
            await SumLocationQuantityAsync(context, sourceStock.LocationId, receiptLine.ItemId));
        Assert.Equal(
            originalStagingQuantity + sourceQuantity,
            await SumLocationQuantityAsync(context, stagingLocationId, receiptLine.ItemId));
        outcomes.Add("picked-to-staging=exact-lot-lpn-owner-status-dimensions");
        await ReconcileCheckpointAsync("supplier-return-pick-completed");

        var packed = await supplierReturnService.PackAsync(
            new SupplierReturnCommandInput(created.Value.Id, "j130-supplier-pack"),
            actorUserId);
        Assert.True(packed.IsSuccess, packed.FirstError?.Message);
        Assert.Equal(SupplierReturnStatus.Packed, packed.Value.Status);
        await ReconcileCheckpointAsync("supplier-return-packed");

        var shipInput = new SupplierReturnShipInput(
            created.Value.Id,
            "J130-CARRIER",
            "J130-TRACKING-001",
            "J130-BOL-001",
            "j130-supplier-ship");
        var shipped = await supplierReturnService.ShipAsync(shipInput, actorUserId);
        Assert.True(shipped.IsSuccess, shipped.FirstError?.Message);
        Assert.Equal(SupplierReturnStatus.Shipped, shipped.Value.Status);
        Assert.Equal(sourceQuantity, shipped.Value.ShippedQuantity);
        await ReconcileCheckpointAsync("supplier-return-shipped");
        var transactionsAfterShip = await context.InventoryTransactions.CountAsync();
        var replay = await supplierReturnService.ShipAsync(shipInput, actorUserId);
        Assert.True(replay.IsSuccess, replay.FirstError?.Message);
        Assert.Equal(sourceQuantity, replay.Value.ShippedQuantity);
        Assert.Equal(transactionsAfterShip, await context.InventoryTransactions.CountAsync());
        outcomes.Add("ship-replay=no-duplicate-inventory-transactions");
        await ReconcileCheckpointAsync("supplier-return-ship-replayed-once");

        var acknowledged = await supplierReturnService.AcknowledgeAsync(
            new SupplierReturnCommandInput(created.Value.Id, "j130-supplier-acknowledge"),
            actorUserId);
        Assert.True(acknowledged.IsSuccess, acknowledged.FirstError?.Message);
        Assert.Equal(SupplierReturnStatus.Acknowledged, acknowledged.Value.Status);
        await ReconcileCheckpointAsync("supplier-return-acknowledged");
        var closed = await supplierReturnService.CloseAsync(
            new SupplierReturnCommandInput(created.Value.Id, "j130-supplier-close"),
            actorUserId);
        Assert.True(closed.IsSuccess, closed.FirstError?.Message);
        Assert.Equal(SupplierReturnStatus.Closed, closed.Value.Status);
        await ReconcileCheckpointAsync("supplier-return-closed");

        var finalItemQuantity = await SumItemQuantityAsync(context, receiptLine.ItemId);
        Assert.Equal(originalItemQuantity - sourceQuantity, finalItemQuantity);
        Assert.Equal(
            originalStagingQuantity,
            await SumLocationQuantityAsync(context, stagingLocationId, receiptLine.ItemId));
        var supplierReturnLedger = await context.InventoryTransactions.AsNoTracking()
            .Where(value => value.ReferenceId == created.Value.ReturnNumber)
            .OrderBy(value => value.TransactionGroupId)
            .ThenBy(value => value.EntrySequence)
            .ToArrayAsync();
        Assert.NotEmpty(supplierReturnLedger);
        Assert.Contains(supplierReturnLedger, value =>
            value.Type == InventoryTransactionType.Reservation &&
            value.ReservedQuantityDelta == sourceQuantity &&
            value.LocationId == sourceStock.LocationId);
        Assert.Contains(supplierReturnLedger, value =>
            value.Type == InventoryTransactionType.Pick &&
            value.QuantityDelta == -sourceQuantity &&
            value.LocationId == sourceStock.LocationId);
        Assert.Contains(supplierReturnLedger, value =>
            value.Type == InventoryTransactionType.Ship &&
            value.QuantityDelta == -sourceQuantity &&
            value.LocationId == stagingLocationId &&
            value.InventoryStatusId == InventoryStatusSystemIds.ReturnPending);
        Assert.All(supplierReturnLedger, value => Assert.Equal(actorUserId, value.ActorUserId));
        var returnCommands = await context.SupplierReturnCommands.AsNoTracking()
            .Where(value => value.SupplierReturnId == created.Value.Id)
            .ToArrayAsync();
        Assert.All(returnCommands, value => Assert.Equal(actorUserId, value.UserId));
        var workCommands = await context.WarehouseWorkCommands.AsNoTracking()
            .Where(value => value.WarehouseWorkId == workId)
            .ToArrayAsync();
        Assert.All(workCommands, value => Assert.Equal(actorUserId, value.UserId));
        outcomes.Add("ledger-and-command-audit=actor-source-document-quantity-qualified");

        return new PostgreSqlJourneyEvidence(
            "supplier-return-shortage-pick-pack-ship-acknowledge",
            "issue-130-supplier-return",
            outcomes,
            [
            "cycle-count execution and variance approval",
            "wave, cluster, cross-dock, kitting, and other disposition journeys",
                "partial quantities, cancellation, hold/release, and stale concurrency tokens",
                "authenticated HTTP boundary for these service journeys"
            ],
            checkpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["sourceQuantityBefore"] = sourceQuantity,
                ["itemQuantityBefore"] = originalItemQuantity,
                ["itemQuantityAfterSupplierShipment"] = finalItemQuantity,
                ["sourceQuantityAfterPick"] = originalSourceQuantity - sourceQuantity,
                ["stagingQuantityAfterShipment"] = originalStagingQuantity
            },
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["supplierReturnCommands"] = returnCommands.Length,
                ["warehouseWorkCommands"] = workCommands.Length,
                ["supplierReturnLedgerEntries"] = supplierReturnLedger.Length
            },
            ReconciliationClean: checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: checkpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: checkpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount));
    }

    private static async Task ReconcileAndRecordAsync(
        string checkpoint,
        WmsDbContext context,
        IInventoryReconciliationService reconciliation,
        List<PostgreSqlJourneyCheckpointEvidence> checkpoints,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var report = await reconciliation.ReconcileAsync(
            new InventoryReconciliationQuery(Deep: true),
            cancellationToken);
        Assert.True(report.IsSuccess, $"{checkpoint}: {report.FirstError?.Message}");
        Assert.True(
            report.Value.IsClean,
            $"{checkpoint}: {string.Join("; ", report.Value.Issues.Select(value => $"{value.Check}:{value.Code}:{value.Message}"))}");
        checkpoints.Add(new PostgreSqlJourneyCheckpointEvidence(
            checkpoint,
            report.Value.IssueCount,
            report.Value.TransactionsScanned,
            ExpectedClean: true));
    }

    private static async Task<decimal> SumItemQuantityAsync(WmsDbContext context, int itemId)
    {
        var rows = await context.Stock.AsNoTracking()
            .Where(value => value.ItemId == itemId)
            .ToArrayAsync();
        return rows.Sum(value => value.QuantityAvailable.Value);
    }

    private static Task<decimal> SumReservedQuantityAsync(WmsDbContext context, int itemId) =>
        context.InventoryBalances.AsNoTracking()
            .Where(value => value.ItemId == itemId)
            .SumAsync(value => value.ReservedQuantity);

    private static async Task<decimal> SumLocationQuantityAsync(
        WmsDbContext context,
        int locationId,
        int itemId)
    {
        var rows = await context.Stock.AsNoTracking()
            .Where(value => value.LocationId == locationId && value.ItemId == itemId)
            .ToArrayAsync();
        return rows.Sum(value => value.QuantityAvailable.Value);
    }

    private static void WriteJourneyEvidence(params PostgreSqlJourneyEvidence[] reports)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_JOURNEY_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (JourneyEvidenceGate)
        {
            var existingReports = File.Exists(path)
                ? JsonSerializer.Deserialize<PostgreSqlJourneyEvidenceEnvelope>(
                    File.ReadAllText(path),
                    JourneyEvidenceJsonOptions)?.Reports ?? []
                : [];
            var json = JsonSerializer.Serialize(
                new PostgreSqlJourneyEvidenceEnvelope(existingReports.Concat(reports).ToArray()),
                JourneyEvidenceJsonOptions);
            File.WriteAllText(path, json);
        }
    }

    private sealed record PostgreSqlJourneyEvidenceEnvelope(
        [property: JsonPropertyName("reports")] IReadOnlyList<PostgreSqlJourneyEvidence> Reports);
}
