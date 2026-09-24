using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.DataGeneration;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Tests.Integration;

public sealed partial class PostgreSqlJourneyTests
{
    [PostgreSqlFact]
    public async Task CycleCountVarianceUsesScannerApprovalMovementLedgerAndReplayOnce()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var seedCheckpoints = new List<PostgreSqlJourneyCheckpointEvidence>();
        var seeded = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-130-cycle-count-journey",
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
        var cycleCounts = services.GetRequiredService<ICycleCountService>();
        var workService = services.GetRequiredService<IWarehouseWorkService>();
        var warehouseId = await context.Warehouses.AsNoTracking()
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var countableBalances = await context.InventoryBalances.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Location)
            .Include(value => value.InventoryStatus)
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Item.IsActive &&
                            !value.Item.RequiresSerial &&
                            value.Location.IsActive &&
                            value.Location.IsCountable &&
                            value.InventoryStatus.IsCountable)
            .OrderBy(value => value.ItemId)
            .ThenBy(value => value.LocationId)
            .ThenBy(value => value.Id)
            .ToArrayAsync();
        var snapshot = countableBalances
            .GroupBy(value => (value.LocationId, value.ItemId))
            .Where(group => group.Count() == 1 &&
                            group.Single().InventoryStatus.IsActive &&
                            group.Single().OnHandQuantity >= 3m &&
                            group.Single().ReservedQuantity <= group.Single().OnHandQuantity - 1m)
            .Select(group => group.Single())
            .First();
        var countedQuantity = snapshot.OnHandQuantity - 1m;
        var plan = await cycleCounts.SavePlanAsync(
            null,
            new CycleCountPlanInput(
                "J130-CYCLE-COUNT-PLAN",
                warehouseId,
                snapshot.LocationId,
                snapshot.ItemId,
                null,
                1,
                0m,
                Blind: true,
                CycleCountFreezePolicy.SnapshotAndReconcile,
                DateTime.UtcNow.AddMinutes(-1)),
            actor.Id);
        Assert.True(plan.IsSuccess, plan.FirstError?.Message);
        await ReconcileAndRecordAsync(
            "cycle-count-plan-created",
            context,
            reconciliation,
            new List<PostgreSqlJourneyCheckpointEvidence>(),
            CancellationToken.None);

        var generated = await cycleCounts.GenerateAsync(
            new CycleCountGenerationQuery(warehouseId, plan.Value.Id),
            actor.Id);
        Assert.True(generated.IsSuccess, generated.FirstError?.Message);
        var summary = Assert.Single(generated.Value.Tasks);
        Assert.Equal(CycleCountTaskStatus.Planned, summary.Status);
        Assert.Null(summary.ExpectedQuantity);
        var initialTask = await cycleCounts.GetTaskAsync(summary.TaskId);
        Assert.True(initialTask.IsSuccess, initialTask.FirstError?.Message);
        var countLine = Assert.Single(initialTask.Value.Lines);
        Assert.Null(countLine.ExpectedQuantity);
        var workId = Assert.IsType<int>(initialTask.Value.WarehouseWorkId);
        var work = await workService.GetAsync(workId);
        Assert.True(work.IsSuccess, work.FirstError?.Message);
        var workLine = Assert.Single(work.Value.Lines);
        Assert.True(countedQuantity > workLine.PlannedQuantity);
        Assert.Equal(countLine.Id, int.Parse(
            workLine.SourceReference!.AsSpan("cycle-count-line:".Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture));
        await ReconcileAndRecordAsync(
            "cycle-count-task-and-work-generated",
            context,
            reconciliation,
            new List<PostgreSqlJourneyCheckpointEvidence>(),
            CancellationToken.None);

        var taskStarted = await cycleCounts.StartTaskAsync(
            summary.TaskId,
            new CycleCountTaskStartInput(initialTask.Value.Revision),
            actor.Id);
        Assert.True(taskStarted.IsSuccess, taskStarted.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.InProgress, taskStarted.Value.Status);
        var assigned = await workService.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput(actor.Id, null, "j130-cycle-count-assign"),
            actor.Id);
        Assert.True(assigned.IsSuccess, assigned.FirstError?.Message);
        var workStarted = await workService.StartAsync(
            workId,
            new WarehouseWorkCommandInput("j130-cycle-count-start"),
            actor.Id);
        Assert.True(workStarted.IsSuccess, workStarted.FirstError?.Message);
        await ReconcileAndRecordAsync(
            "cycle-count-task-and-work-started",
            context,
            reconciliation,
            new List<PostgreSqlJourneyCheckpointEvidence>(),
            CancellationToken.None);

        var workCompleted = await workService.CompleteAsync(
            workId,
            new WarehouseWorkCompletionInput(
                "j130-cycle-count-complete",
                CompletionReference: "J130 cycle count physical scan",
                Scans:
                [
                    new WarehouseWorkScanInput(
                        workLine.Id,
                        workLine.ItemId,
                        workLine.SourceLocationId!.Value,
                        workLine.DestinationLocationId!.Value,
                        countedQuantity,
                        workLine.LicensePlateId,
                        LotId: workLine.LotId,
                        SerialNumberId: workLine.SerialNumberId,
                        SerialNumber: workLine.SerialNumber)
                ]),
            actor.Id);
        Assert.True(workCompleted.IsSuccess, workCompleted.FirstError?.Message);
        Assert.Equal(WarehouseWorkStatus.Completed, workCompleted.Value.Status);
        var submitted = await cycleCounts.GetTaskAsync(summary.TaskId);
        Assert.True(submitted.IsSuccess, submitted.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.AwaitingApproval, submitted.Value.Status);
        Assert.Equal(countedQuantity, submitted.Value.Lines.Single().CountedQuantity);
        Assert.Empty(await context.Movements.AsNoTracking()
            .Where(value => value.ReferenceNumber == submitted.Value.TaskNumber &&
                            value.Type == MovementType.CycleCount)
            .ToArrayAsync());
        Assert.Equal(snapshot.OnHandQuantity, await context.InventoryBalances.AsNoTracking()
            .Where(value => value.Id == snapshot.Id)
            .Select(value => value.OnHandQuantity)
            .SingleAsync());
        await ReconcileAndRecordAsync(
            "cycle-count-submitted-no-variance-applied-before-approval",
            context,
            reconciliation,
            new List<PostgreSqlJourneyCheckpointEvidence>(),
            CancellationToken.None);

        var approved = await cycleCounts.ApproveTaskAsync(
            summary.TaskId,
            new CycleCountTaskApprovalInput(submitted.Value.Revision, "Issue 130 verified physical count"),
            actor.Id);
        Assert.True(approved.IsSuccess, approved.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.Completed, approved.Value.Status);
        Assert.Equal(snapshot.OnHandQuantity, approved.Value.Lines.Single().ExpectedQuantity);
        Assert.Equal(countedQuantity, approved.Value.Lines.Single().CountedQuantity);
        Assert.Equal(-1m, approved.Value.Lines.Single().VarianceQuantity);
        await ReconcileAndRecordAsync(
            "cycle-count-variance-approved-and-reconciled",
            context,
            reconciliation,
            new List<PostgreSqlJourneyCheckpointEvidence>(),
            CancellationToken.None);

        var ledger = await context.InventoryTransactions.AsNoTracking()
            .Where(value => value.ReferenceType == "CycleCountLine" &&
                            value.ReferenceId == countLine.Id.ToString(CultureInfo.InvariantCulture) &&
                            value.Type == InventoryTransactionType.CountVariance)
            .ToArrayAsync();
        Assert.Single(ledger);
        Assert.Equal(-1m, ledger.Single().QuantityDelta);
        Assert.Equal(snapshot.OnHandQuantity, ledger.Single().QuantityBefore);
        Assert.Equal(countedQuantity, ledger.Single().QuantityAfter);
        Assert.Equal(actor.Id, ledger.Single().ActorUserId);
        var movement = await context.Movements.AsNoTracking()
            .SingleAsync(value => value.ReferenceNumber == approved.Value.TaskNumber &&
                                  value.Type == MovementType.CycleCount);
        Assert.Equal(-1m, movement.AdjustmentDelta);
        Assert.Equal(snapshot.OnHandQuantity, movement.AdjustmentBeforeQuantity);
        Assert.Equal(countedQuantity, movement.AdjustmentAfterQuantity);
        Assert.Equal(movement.Id, ledger.Single().MovementId);

        var transactionCount = await context.InventoryTransactions.CountAsync();
        var approvalReplay = await cycleCounts.ApproveTaskAsync(
            summary.TaskId,
            new CycleCountTaskApprovalInput(1, "duplicate replay"),
            actor.Id);
        Assert.True(approvalReplay.IsSuccess, approvalReplay.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.Completed, approvalReplay.Value.Status);
        Assert.Equal(transactionCount, await context.InventoryTransactions.CountAsync());
        await ReconcileAndRecordAsync(
            "cycle-count-approval-replayed-once",
            context,
            reconciliation,
            new List<PostgreSqlJourneyCheckpointEvidence>(),
            CancellationToken.None);
    }
}
