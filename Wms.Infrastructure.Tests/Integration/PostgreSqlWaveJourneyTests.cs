using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.DataGeneration;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Outbound;
using Wms.Application.SalesOrders;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Tests.Integration;

public sealed partial class PostgreSqlJourneyTests
{
    [PostgreSqlFact]
    public async Task WaveAllocationReleaseReplayAndCancellationReconcileEveryTransition()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-130-wave-journey",
                Environment: "Testing",
                Locale: "en-US"));
        Assert.True(seeded.Report.ReconciliationClean);

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
        var salesOrders = services.GetRequiredService<ISalesOrderService>();
        var waves = services.GetRequiredService<IWaveService>();
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var outcomes = new List<string>();

        var warehouseId = await context.Warehouses.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var customerId = await context.Customers.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var candidates = await context.Stock.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Where(value => value.Location.WarehouseId == warehouseId &&
                            value.Location.IsPickable &&
                            value.Item.IsActive &&
                            value.Item.RequiresLot == false &&
                            value.Item.RequiresSerial == false &&
                            value.LotId == null &&
                            value.SerialNumberId == null &&
                            value.LicensePlateId == null &&
                            value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                            value.OwnerKind == InventoryOwnerKind.CompanyOwned)
            .ToArrayAsync();
        var candidate = candidates
            .Where(value => value.QuantityAvailable.Value >= 1m)
            .OrderByDescending(value => value.QuantityAvailable.Value)
            .ThenBy(value => value.ItemId)
            .ThenBy(value => value.LocationId)
            .FirstOrDefault();
        Assert.NotNull(candidate);

        async Task<(decimal OnHand, decimal Reserved, decimal Available)> ReadBalanceAsync()
        {
            var balances = await context.InventoryBalances.AsNoTracking()
                .Where(value => value.WarehouseId == warehouseId && value.ItemId == candidate.ItemId)
                .ToArrayAsync();
            return (
                balances.Sum(value => value.OnHandQuantity),
                balances.Sum(value => value.ReservedQuantity),
                balances.Sum(value => value.AvailableQuantity));
        }

        var beforeAllocation = await ReadBalanceAsync();
        var primaryOrder = await salesOrders.CreateAsync(
            new SalesOrderInput(
                warehouseId,
                customerId,
                OrderDate: new DateOnly(2026, 1, 15),
                RequestedShipDate: new DateOnly(2026, 1, 16),
                ExternalReference: "J130-WAVE-ORDER-01",
                SourceType: "ISSUE-130-WAVE-JOURNEY",
                AllowPartialShipment: false,
                Lines: [new SalesOrderLineInput(candidate.Item.Sku, 1m, candidate.Item.UnitOfMeasure)]),
            actor.Id);
        Assert.True(primaryOrder.IsSuccess, primaryOrder.Error);
        await ReconcileAndRecordAsync(
            "wave-order-created",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        primaryOrder = await salesOrders.ConfirmAsync(primaryOrder.Value.Id, actor.Id);
        Assert.True(primaryOrder.IsSuccess, primaryOrder.Error);
        await ReconcileAndRecordAsync(
            "wave-order-confirmed",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var created = await waves.CreateAsync(
            new WaveCreateInput(
                warehouseId,
                "J130-WAVE-CREATE-01",
                CustomerId: customerId,
                Limit: 10,
                ReleaseToWarehouse: true),
            actor.Id);
        Assert.True(created.IsSuccess, created.Error);
        var selectedLine = Assert.Single(created.Value.Lines);
        Assert.Equal(primaryOrder.Value.Lines.Single().Id, selectedLine.SalesOrderLineId);
        Assert.Equal(WaveLineStatus.Selected, selectedLine.Status);
        await ReconcileAndRecordAsync(
            "wave-demand-selected",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var processInput = new WaveProcessInput("J130-WAVE-PROCESS-01", ReleaseToWarehouse: true);
        var processed = await waves.ProcessAsync(created.Value.Id, processInput, actor.Id);
        Assert.True(processed.IsSuccess, processed.Error);
        Assert.Equal(WaveStatus.Released, processed.Value.Status);
        var processedLine = Assert.Single(processed.Value.Lines);
        Assert.Equal(WaveLineStatus.Released, processedLine.Status);
        Assert.Equal(1m, processedLine.AllocatedQuantity);
        Assert.True(processedLine.WorkCount > 0);
        Assert.NotNull(processedLine.ReservationId);
        var reservationAllocationCount = await context.InventoryReservationAllocations.AsNoTracking()
            .CountAsync(value => value.ReservationId == processedLine.ReservationId);
        Assert.True(reservationAllocationCount > 0);
        var afterAllocation = await ReadBalanceAsync();
        Assert.Equal(beforeAllocation.OnHand, afterAllocation.OnHand);
        Assert.Equal(beforeAllocation.Reserved + 1m, afterAllocation.Reserved);
        await ReconcileAndRecordAsync(
            "wave-allocation-released",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var replay = await waves.ProcessAsync(created.Value.Id, processInput, actor.Id);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(WaveStatus.Released, replay.Value.Status);
        Assert.Equal(processedLine.ReservationId, Assert.Single(replay.Value.Lines).ReservationId);
        var replayAllocationCount = await context.InventoryReservationAllocations.AsNoTracking()
            .CountAsync(value => value.ReservationId == processedLine.ReservationId);
        Assert.Equal(reservationAllocationCount, replayAllocationCount);
        var afterReplay = await ReadBalanceAsync();
        Assert.Equal(afterAllocation, afterReplay);
        await ReconcileAndRecordAsync(
            "wave-process-replayed",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        outcomes.Add("wave=selected,allocated,released,process-replay-preserved-reservation-and-stock");

        var cancellationOrder = await salesOrders.CreateAsync(
            new SalesOrderInput(
                warehouseId,
                customerId,
                OrderDate: new DateOnly(2026, 1, 15),
                RequestedShipDate: new DateOnly(2026, 1, 16),
                ExternalReference: "J130-WAVE-ORDER-02",
                SourceType: "ISSUE-130-WAVE-JOURNEY",
                AllowPartialShipment: false,
                Lines: [new SalesOrderLineInput(candidate.Item.Sku, 1m, candidate.Item.UnitOfMeasure)]),
            actor.Id);
        Assert.True(cancellationOrder.IsSuccess, cancellationOrder.Error);
        await ReconcileAndRecordAsync(
            "wave-cancellation-order-created",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        cancellationOrder = await salesOrders.ConfirmAsync(cancellationOrder.Value.Id, actor.Id);
        Assert.True(cancellationOrder.IsSuccess, cancellationOrder.Error);
        await ReconcileAndRecordAsync(
            "wave-cancellation-order-confirmed",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var cancellationWave = await waves.CreateAsync(
            new WaveCreateInput(
                warehouseId,
                "J130-WAVE-CREATE-02",
                CustomerId: customerId,
                Limit: 10,
                ReleaseToWarehouse: true),
            actor.Id);
        Assert.True(cancellationWave.IsSuccess, cancellationWave.Error);
        var cancellationLine = Assert.Single(cancellationWave.Value.Lines);
        Assert.Equal(cancellationOrder.Value.Lines.Single().Id, cancellationLine.SalesOrderLineId);
        await ReconcileAndRecordAsync(
            "wave-cancellation-demand-selected",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var removed = await waves.RemoveLineAsync(
            cancellationWave.Value.Id,
            cancellationLine.SalesOrderLineId,
            new WaveLineRemovalInput("J130-WAVE-REMOVE-02", "Order was held before wave release."),
            actor.Id);
        Assert.True(removed.IsSuccess, removed.Error);
        Assert.Equal(WaveLineStatus.Removed, Assert.Single(removed.Value.Lines).Status);
        await ReconcileAndRecordAsync(
            "wave-line-removed-before-processing",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var cancelled = await waves.CancelAsync(
            cancellationWave.Value.Id,
            new WaveCancellationInput("J130-WAVE-CANCEL-02", "Operator cancelled the held wave."),
            actor.Id);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(WaveStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal(WaveLineStatus.Removed, Assert.Single(cancelled.Value.Lines).Status);
        await ReconcileAndRecordAsync(
            "wave-cancelled",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        outcomes.Add("wave=unprocessed-demand-line-removed-and-wave-cancelled");

        WriteJourneyEvidence(new PostgreSqlJourneyEvidence(
            "wave-allocation-release-replay-and-cancellation",
            target.TargetIdentifier,
            outcomes,
            [
                "cluster, cross-dock, and kitting wave orchestration",
                "partial allocation, shortage recovery, and hold/release interactions",
                "authenticated MVC/browser wave boundary"
            ],
            checkpoints,
            new Dictionary<string, decimal>
            {
                ["itemOnHandBeforeWave"] = beforeAllocation.OnHand,
                ["itemOnHandAfterWave"] = afterAllocation.OnHand,
                ["itemReservedBeforeWave"] = beforeAllocation.Reserved,
                ["itemReservedAfterWave"] = afterAllocation.Reserved,
                ["itemAvailableBeforeWave"] = beforeAllocation.Available,
                ["itemAvailableAfterWave"] = afterAllocation.Available,
                ["waveAllocatedQuantity"] = processedLine.AllocatedQuantity
            },
            new Dictionary<string, int>
            {
                ["waveSelectedLines"] = created.Value.SelectedLineCount,
                ["waveReleasedLines"] = processed.Value.Lines.Count(value => value.Status == WaveLineStatus.Released),
                ["wavePickWorks"] = processedLine.WorkCount,
                ["reservationAllocationRows"] = reservationAllocationCount,
                ["cancelledWaves"] = 1,
                ["checkpoints"] = checkpoints.Count
            },
            ReconciliationClean: checkpoints.All(value => value.IssueCount == 0),
            UnexpectedReconciliationIssueCount: checkpoints.Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: 0));
    }

    [PostgreSqlFact]
    public async Task SalesOrderHoldReleaseAndCancellationReconcileEveryTransition()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-130-sales-order-hold-journey",
                Environment: "Testing",
                Locale: "en-US"));
        Assert.True(seeded.Report.ReconciliationClean);

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
        var salesOrders = services.GetRequiredService<ISalesOrderService>();
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var warehouseId = await context.Warehouses.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var customerId = await context.Customers.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var item = await context.Items.AsNoTracking()
            .Where(value => value.IsActive &&
                            value.RequiresLot == false &&
                            value.RequiresSerial == false)
            .OrderBy(value => value.Id)
            .Select(value => new { value.Id, value.Sku, value.UnitOfMeasure })
            .FirstAsync();

        async Task<(decimal OnHand, decimal Reserved, decimal Available)> ReadInventoryTotalsAsync()
        {
            var balances = await context.InventoryBalances.AsNoTracking()
                .Where(value => value.WarehouseId == warehouseId)
                .ToArrayAsync();
            return (
                balances.Sum(value => value.OnHandQuantity),
                balances.Sum(value => value.ReservedQuantity),
                balances.Sum(value => value.AvailableQuantity));
        }

        var balancesBefore = await ReadInventoryTotalsAsync();
        var inventoryTransactionCountBefore = await context.InventoryTransactions.AsNoTracking()
            .CountAsync(value => value.WarehouseId == warehouseId);
        var checkpointsAdded = new List<string>();

        async Task ReconcileAsync(string name)
        {
            await ReconcileAndRecordAsync(
                name,
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);
            checkpointsAdded.Add(name);
        }

        var created = await salesOrders.CreateAsync(
            new SalesOrderInput(
                warehouseId,
                customerId,
                OrderDate: new DateOnly(2026, 1, 15),
                RequestedShipDate: new DateOnly(2026, 1, 16),
                ExternalReference: "J130-HOLD-ORDER-01",
                SourceType: "ISSUE-130-HOLD-JOURNEY",
                AllowPartialShipment: false,
                Lines: [new SalesOrderLineInput(item.Sku, 1m, item.UnitOfMeasure)]),
            actor.Id);
        Assert.True(created.IsSuccess, created.Error);
        Assert.Equal(SalesOrderStatus.Draft, created.Value.Status);
        await ReconcileAsync("sales-order-hold-created");

        var confirmed = await salesOrders.ConfirmAsync(created.Value.Id, actor.Id);
        Assert.True(confirmed.IsSuccess, confirmed.Error);
        Assert.Equal(SalesOrderStatus.Confirmed, confirmed.Value.Status);
        await ReconcileAsync("sales-order-hold-confirmed");

        var held = await salesOrders.HoldAsync(
            confirmed.Value.Id,
            "Customer requested a temporary hold.",
            actor.Id);
        Assert.True(held.IsSuccess, held.Error);
        Assert.Equal(SalesOrderStatus.Held, held.Value.Status);
        await ReconcileAsync("sales-order-held");

        var released = await salesOrders.ReleaseHoldAsync(held.Value.Id, actor.Id);
        Assert.True(released.IsSuccess, released.Error);
        Assert.Equal(SalesOrderStatus.Confirmed, released.Value.Status);
        await ReconcileAsync("sales-order-hold-released");

        var cancelled = await salesOrders.CancelAsync(released.Value.Id, actor.Id);
        Assert.True(cancelled.IsSuccess, cancelled.Error);
        Assert.Equal(SalesOrderStatus.Cancelled, cancelled.Value.Status);
        await ReconcileAsync("sales-order-cancelled-after-release");

        var persisted = await context.SalesOrders.AsNoTracking()
            .SingleAsync(value => value.Id == created.Value.Id);
        Assert.Equal(SalesOrderStatus.Cancelled, persisted.Status);
        Assert.True(persisted.Revision >= 4);
        var balancesAfter = await ReadInventoryTotalsAsync();
        Assert.Equal(balancesBefore, balancesAfter);
        Assert.Equal(
            inventoryTransactionCountBefore,
            await context.InventoryTransactions.AsNoTracking().CountAsync(value => value.WarehouseId == warehouseId));

        var auditActions = new[]
        {
            WmsAuditActions.SalesOrderCreated,
            WmsAuditActions.SalesOrderConfirmed,
            WmsAuditActions.SalesOrderHeld,
            WmsAuditActions.SalesOrderHoldReleased,
            WmsAuditActions.SalesOrderCancelled
        };
        var auditEntries = await context.AuditEntries.AsNoTracking()
            .Where(value => value.EntityId == created.Value.DocumentNumber &&
                            auditActions.Contains(value.Action))
            .OrderBy(value => value.Id)
            .ToArrayAsync();
        Assert.Equal(auditActions.Length, auditEntries.Length);
        Assert.Equal(auditActions, auditEntries.Select(value => value.Action));
        Assert.All(auditEntries, entry =>
        {
            Assert.Equal(actor.Id, entry.ActorUserId);
            Assert.Equal(warehouseId, entry.WarehouseId);
            Assert.Equal(WmsAuditEntityTypes.SalesOrder, entry.EntityType);
            Assert.True(entry.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId));
        });
        using (var heldAudit = JsonDocument.Parse(auditEntries[2].AfterJson!))
        {
            Assert.Equal(SalesOrderStatus.Held.ToString(), heldAudit.RootElement.GetProperty("status").GetString());
        }
        using (var cancelledAudit = JsonDocument.Parse(auditEntries[4].AfterJson!))
        {
            Assert.Equal(SalesOrderStatus.Cancelled.ToString(), cancelledAudit.RootElement.GetProperty("status").GetString());
        }

        WriteJourneyEvidence(new PostgreSqlJourneyEvidence(
            "sales-order-hold-release-and-cancellation",
            target.TargetIdentifier,
            ["sales-order=draft,confirmed,held,released-to-confirmed,cancelled"],
            ["authenticated MVC/browser hold controls", "allocation hold after reservation and picked-work recovery"],
            checkpoints,
            new Dictionary<string, decimal>(),
            new Dictionary<string, int>
            {
                ["persistedOrderCount"] = 1,
                ["auditedTransitions"] = auditEntries.Length,
                ["inventoryTransactionsAdded"] = 0,
                ["reconciliationCheckpoints"] = checkpointsAdded.Count
            },
            ReconciliationClean: checkpoints.All(value => value.IssueCount == 0),
            UnexpectedReconciliationIssueCount: checkpoints.Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: 0));
    }

    [PostgreSqlFact]
    public async Task SalesOrderPartialAllocationShortageAndReleaseReplayReconcileEveryTransition()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-130-partial-allocation-journey",
                Environment: "Testing",
                Locale: "en-US"));
        Assert.True(seeded.Report.ReconciliationClean);

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
        var salesOrders = services.GetRequiredService<ISalesOrderService>();
        var allocationService = services.GetRequiredService<ISalesOrderAllocationService>();
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var warehouseId = await context.Warehouses.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var customerId = await context.Customers.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var inventoryRows = await context.Stock.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Where(value => value.Location.WarehouseId == warehouseId &&
                            value.Location.IsPickable &&
                            value.Item.IsActive &&
                            value.Item.RequiresLot == false &&
                            value.Item.RequiresSerial == false &&
                            value.Item.RequiresExpiry == false &&
                            value.LotId == null &&
                            value.SerialNumberId == null &&
                            value.LicensePlateId == null &&
                            value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                            value.OwnerKind == InventoryOwnerKind.CompanyOwned)
            .ToArrayAsync();
        var item = inventoryRows
            .GroupBy(value => new { value.ItemId, value.Item.Sku, value.Item.UnitOfMeasure })
            .Select(group => new
            {
                group.Key.ItemId,
                group.Key.Sku,
                group.Key.UnitOfMeasure,
                AvailableQuantity = group.Sum(value => value.QuantityAvailable.Value)
            })
            .Where(value => value.AvailableQuantity >= 1m)
            .OrderByDescending(value => value.AvailableQuantity)
            .ThenBy(value => value.ItemId)
            .FirstOrDefault();
        Assert.NotNull(item);
        var orderedQuantity = item.AvailableQuantity + 1m;

        async Task<(decimal OnHand, decimal Reserved, decimal Available)> ReadBalanceAsync()
        {
            var balances = await context.InventoryBalances.AsNoTracking()
                .Where(value => value.WarehouseId == warehouseId && value.ItemId == item.ItemId)
                .ToArrayAsync();
            return (
                balances.Sum(value => value.OnHandQuantity),
                balances.Sum(value => value.ReservedQuantity),
                balances.Sum(value => value.AvailableQuantity));
        }

        var beforeAllocation = await ReadBalanceAsync();
        var order = await salesOrders.CreateAsync(
            new SalesOrderInput(
                warehouseId,
                customerId,
                OrderDate: new DateOnly(2026, 1, 15),
                RequestedShipDate: new DateOnly(2026, 1, 16),
                ExternalReference: "J130-PARTIAL-ORDER-01",
                SourceType: "ISSUE-130-PARTIAL-JOURNEY",
                AllowPartialShipment: true,
                Lines: [new SalesOrderLineInput(item.Sku, orderedQuantity, item.UnitOfMeasure)]),
            actor.Id);
        Assert.True(order.IsSuccess, order.Error);
        await ReconcileAndRecordAsync(
            "partial-order-created",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        order = await salesOrders.ConfirmAsync(order.Value.Id, actor.Id);
        Assert.True(order.IsSuccess, order.Error);
        await ReconcileAndRecordAsync(
            "partial-order-confirmed",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var allocationCommand = new SalesOrderAllocationCommand(
            [order.Value.Lines.Single().Id],
            ReleaseToWarehouse: false,
            IdempotencyKey: "J130-PARTIAL-ALLOCATE-01");
        var allocated = await allocationService.AllocateAsync(order.Value.Id, allocationCommand, actor.Id);
        Assert.True(allocated.IsSuccess, allocated.Error);
        Assert.Equal(SalesOrderStatus.PartiallyAllocated, allocated.Value.OrderStatus);
        var allocatedLine = Assert.Single(allocated.Value.Lines);
        Assert.Equal(item.AvailableQuantity, allocatedLine.AllocatedBaseQuantity);
        Assert.Equal(1m, allocatedLine.BackorderBaseQuantity);
        Assert.NotNull(allocatedLine.ReservationId);
        Assert.Empty(allocated.Value.Work);
        var allocationRows = await context.InventoryReservationAllocations.AsNoTracking()
            .Include(value => value.Location)
            .Where(value => value.ReservationId == allocatedLine.ReservationId)
            .OrderBy(value => value.Id)
            .ToArrayAsync();
        var allocationRowCount = allocationRows.Length;
        Assert.True(allocationRowCount > 0);
        Assert.Equal(allocatedLine.AllocatedBaseQuantity, allocationRows.Sum(value => value.AllocatedQuantity));
        Assert.All(allocationRows, value =>
        {
            Assert.Equal(warehouseId, value.WarehouseId);
            Assert.Equal(item.ItemId, value.ItemId);
            Assert.True(value.Location.IsPickable);
            Assert.Null(value.LotId);
            Assert.Null(value.SerialNumberId);
            Assert.Null(value.SerialNumber);
            Assert.Null(value.LicensePlateId);
            Assert.Equal(InventoryStatusSystemIds.Available, value.InventoryStatusId);
            Assert.Equal(InventoryOwnerKind.CompanyOwned, value.OwnerKind);
            Assert.Null(value.InventoryOwnerId);
            Assert.Equal(InventoryOwnershipDimension.CompanyOwnerCode, value.OwnerCodeSnapshot);
        });
        var afterAllocation = await ReadBalanceAsync();
        Assert.Equal(beforeAllocation.OnHand, afterAllocation.OnHand);
        Assert.Equal(beforeAllocation.Reserved + allocatedLine.AllocatedBaseQuantity, afterAllocation.Reserved);
        await ReconcileAndRecordAsync(
            "partial-order-allocated-with-backorder",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var allocationReplay = await allocationService.AllocateAsync(order.Value.Id, allocationCommand, actor.Id);
        Assert.True(allocationReplay.IsSuccess, allocationReplay.Error);
        Assert.Equal(allocatedLine.AllocatedBaseQuantity, allocationReplay.Value.Lines.Single().AllocatedBaseQuantity);
        Assert.Equal(allocatedLine.BackorderBaseQuantity, allocationReplay.Value.Lines.Single().BackorderBaseQuantity);
        Assert.Equal(
            allocationRowCount,
            await context.InventoryReservationAllocations.AsNoTracking()
                .CountAsync(value => value.ReservationId == allocatedLine.ReservationId));
        var afterAllocationReplay = await ReadBalanceAsync();
        Assert.Equal(afterAllocation, afterAllocationReplay);
        await ReconcileAndRecordAsync(
            "partial-order-allocation-replayed",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var releaseCommand = new SalesOrderAllocationCommand(IdempotencyKey: "J130-PARTIAL-RELEASE-01");
        var released = await allocationService.ReleaseAsync(order.Value.Id, releaseCommand, actor.Id);
        Assert.True(released.IsSuccess, released.Error);
        Assert.Equal(SalesOrderStatus.Released, released.Value.OrderStatus);
        Assert.Equal(allocatedLine.AllocatedBaseQuantity, released.Value.Work.Sum(value => value.PlannedQuantity));
        var workCount = await context.WarehouseWorks.AsNoTracking()
            .CountAsync(value => value.SourceEntityType == "SalesOrderLine" &&
                                 value.SourceEntityId == allocatedLine.LineId.ToString(CultureInfo.InvariantCulture));
        Assert.True(workCount > 0);
        await ReconcileAndRecordAsync(
            "partial-order-pick-work-released",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var releaseReplay = await allocationService.ReleaseAsync(order.Value.Id, releaseCommand, actor.Id);
        Assert.True(releaseReplay.IsSuccess, releaseReplay.Error);
        Assert.Equal(SalesOrderStatus.Released, releaseReplay.Value.OrderStatus);
        Assert.Equal(
            workCount,
            await context.WarehouseWorks.AsNoTracking()
                .CountAsync(value => value.SourceEntityType == "SalesOrderLine" &&
                                     value.SourceEntityId == allocatedLine.LineId.ToString(CultureInfo.InvariantCulture)));
        Assert.Equal(
            allocationRowCount,
            await context.InventoryReservationAllocations.AsNoTracking()
                .CountAsync(value => value.ReservationId == allocatedLine.ReservationId));
        var afterReleaseReplay = await ReadBalanceAsync();
        Assert.Equal(afterAllocation, afterReleaseReplay);
        await ReconcileAndRecordAsync(
            "partial-order-release-replayed",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var allocationAudits = await context.AuditEntries.AsNoTracking()
            .Where(value => value.Action == WmsAuditActions.AllocationChanged &&
                            value.EntityId == order.Value.DocumentNumber)
            .ToArrayAsync();
        Assert.True(allocationAudits.Length >= 2);
        Assert.All(allocationAudits, entry =>
        {
            Assert.Equal(actor.Id, entry.ActorUserId);
            Assert.Equal(warehouseId, entry.WarehouseId);
            Assert.Equal(WmsAuditEntityTypes.Allocation, entry.EntityType);
            Assert.True(entry.Succeeded);
        });

        WriteJourneyEvidence(new PostgreSqlJourneyEvidence(
            "sales-order-partial-allocation-shortage-and-release-replay",
            target.TargetIdentifier,
            ["allocation=partial-with-one-unit-backorder,allocation-replay-once,pick-work-release-and-replay-once"],
            ["backorder replenishment and eventual completion", "partial allocation for lot/serial/license-plate dimensions"],
            checkpoints,
            new Dictionary<string, decimal>
            {
                ["itemOnHandBeforeAllocation"] = beforeAllocation.OnHand,
                ["itemOnHandAfterAllocation"] = afterAllocation.OnHand,
                ["itemReservedBeforeAllocation"] = beforeAllocation.Reserved,
                ["itemReservedAfterAllocation"] = afterAllocation.Reserved,
                ["itemAvailableBeforeAllocation"] = beforeAllocation.Available,
                ["itemAvailableAfterAllocation"] = afterAllocation.Available,
                ["eligiblePickableAvailableBeforeAllocation"] = item.AvailableQuantity,
                ["orderedQuantity"] = orderedQuantity,
                ["allocatedQuantity"] = allocatedLine.AllocatedBaseQuantity,
                ["backorderQuantity"] = allocatedLine.BackorderBaseQuantity
            },
            new Dictionary<string, int>
            {
                ["reservationAllocationRows"] = allocationRowCount,
                ["pickWorkItems"] = workCount,
                ["allocationAuditEvents"] = allocationAudits.Length,
                ["checkpoints"] = checkpoints.Count
            },
            ReconciliationClean: checkpoints.All(value => value.IssueCount == 0),
            UnexpectedReconciliationIssueCount: checkpoints.Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: 0));
    }
}
