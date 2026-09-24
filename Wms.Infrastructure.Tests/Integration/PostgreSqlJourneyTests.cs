using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.DataGeneration;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Transfers;
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

public sealed class PostgreSqlJourneyTests
{
    private static readonly int[] TransferEntrySequences = [1, 2];

    private static readonly JsonSerializerOptions JourneyEvidenceJsonOptions = new()
    {
        WriteIndented = true
    };

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
        var unsupportedOrSkippedScenarios = new[]
        {
            "ASN and quality inspection lifecycle",
            "supplier return lifecycle",
            "cycle-count execution and variance approval",
            "replenishment execution",
            "wave, cluster, cross-dock, kitting, and disposition journeys",
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
        WriteJourneyEvidence(generatedLifecycle, transferJourney);
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

        var json = JsonSerializer.Serialize(
            new { reports },
            JourneyEvidenceJsonOptions);
        File.WriteAllText(path, json);
    }
}
