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
    long TransactionsScanned);

public sealed record PostgreSqlJourneyEvidence(
    string Scenario,
    string TargetIdentifier,
    IReadOnlyList<string> OperationOutcomes,
    IReadOnlyList<PostgreSqlJourneyCheckpointEvidence> Checkpoints,
    IReadOnlyDictionary<string, decimal> ActualQuantities,
    bool ReconciliationClean,
    int ReconciliationIssueCount);

public sealed class PostgreSqlJourneyTests
{
    private static readonly JsonSerializerOptions JourneyEvidenceJsonOptions = new()
    {
        WriteIndented = true
    };

    [PostgreSqlFact]
    public async Task CrossWarehouseTransferReconcilesEveryTransitionAndReplaysOnce()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-130-transfer-journey",
                Environment: "Testing",
                Locale: "en-US"));

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
                report.Value.TransactionsScanned));
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

        Assert.Equal(8, checkpoints.Count);
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
        Assert.True(checkpoints.All(value => value.IssueCount == 0));
        WriteJourneyEvidence(new PostgreSqlJourneyEvidence(
            "cross-warehouse-transfer",
            target.TargetIdentifier,
            outcomes,
            checkpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["itemQuantityBefore"] = originalItemQuantity,
                ["itemQuantityAfter"] = finalItemQuantity,
                ["sourceLocationBefore"] = originalSourceLocationQuantity,
                ["sourceLocationAfter"] = finalSourceLocationQuantity,
                ["transitAfterShip"] = transitAfterShip,
                ["transitAfterReceive"] = finalTransitQuantity,
                ["destinationLocationBefore"] = originalDestinationLocationQuantity,
                ["destinationLocationAfter"] = finalDestinationLocationQuantity
            },
            ReconciliationClean: checkpoints.All(value => value.IssueCount == 0),
            ReconciliationIssueCount: checkpoints.Sum(value => value.IssueCount)));
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

    private static void WriteJourneyEvidence(PostgreSqlJourneyEvidence report)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_POSTGRES_JOURNEY_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var json = JsonSerializer.Serialize(
            new { reports = new[] { report } },
            JourneyEvidenceJsonOptions);
        File.WriteAllText(path, json);
    }
}
