using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.DataGeneration;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.UseCases.Inventory;
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
        var checkpoints = new List<PostgreSqlJourneyCheckpointEvidence>(seedCheckpoints);
        var outcomes = new List<string>(seeded.Report.OperationOutcomes);

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
                            !value.Item.RequiresLot &&
                            !value.Item.RequiresSerial &&
                            value.Location.IsActive &&
                            value.Location.IsCountable &&
                            value.InventoryStatus.IsCountable)
            .OrderBy(value => value.ItemId)
            .ThenBy(value => value.LocationId)
            .ThenBy(value => value.Id)
            .ToArrayAsync();
        var balanceToIncrease = countableBalances
            .GroupBy(value => (value.LocationId, value.ItemId))
            .Where(group => group.Count() == 1 &&
                            group.Single().InventoryStatus.IsActive &&
                            group.Single().OnHandQuantity > 0m &&
                            group.Single().ReservedQuantity <= group.Single().OnHandQuantity)
            .Select(group => group.Single())
            .FirstOrDefault();
        Assert.NotNull(balanceToIncrease);
        var balanceIncrease = await services.GetRequiredService<IStockAdjustmentUseCase>().ExecuteAsync(
            new StockAdjustmentDto(
                balanceToIncrease.Item.Sku,
                balanceToIncrease.Location.Code,
                3m,
                "Issue 130 count-variance test balance",
                UnitOfMeasure: balanceToIncrease.BaseUnitOfMeasure),
            actor.Id);
        Assert.True(balanceIncrease.IsSuccess, balanceIncrease.FirstError?.Message);
        outcomes.Add("count-balance=raised-through-stock-adjustment-command");
        await ReconcileAndRecordAsync(
            "cycle-count-test-balance-adjusted",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        var snapshot = await context.InventoryBalances.AsNoTracking()
            .SingleAsync(value => value.Id == balanceToIncrease.Id);
        Assert.True(snapshot.OnHandQuantity >= 3m);
        Assert.True(snapshot.ReservedQuantity <= snapshot.OnHandQuantity - 1m);
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
                new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc)),
            actor.Id);
        Assert.True(plan.IsSuccess, plan.FirstError?.Message);
        await ReconcileAndRecordAsync(
            "cycle-count-plan-created",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var generated = await cycleCounts.GenerateAsync(
            new CycleCountGenerationQuery(warehouseId, plan.Value.Id),
            actor.Id);
        Assert.True(generated.IsSuccess, generated.FirstError?.Message);
        var summary = Assert.Single(generated.Value.Tasks);
        Assert.Equal(CycleCountTaskStatus.Planned, summary.Status);
        Assert.Null(summary.ExpectedQuantity);
        outcomes.Add("count-plan=created,task-generated,blind-expected-quantity-hidden");
        var initialTask = await cycleCounts.GetTaskAsync(summary.TaskId);
        Assert.True(initialTask.IsSuccess, initialTask.FirstError?.Message);
        var countLine = Assert.Single(initialTask.Value.Lines);
        Assert.Null(countLine.ExpectedQuantity);
        var workId = Assert.IsType<int>(initialTask.Value.WarehouseWorkId);
        var work = await workService.GetAsync(workId);
        Assert.True(work.IsSuccess, work.FirstError?.Message);
        var workLine = Assert.Single(work.Value.Lines);
        Assert.True(countedQuantity > workLine.PlannedQuantity);
        Assert.Equal(snapshot.WarehouseId, workLine.WarehouseId);
        Assert.Equal(snapshot.ItemId, workLine.ItemId);
        Assert.Equal(snapshot.LocationId, workLine.SourceLocationId);
        Assert.Equal(snapshot.LocationId, workLine.DestinationLocationId);
        Assert.Equal(snapshot.LotId, workLine.LotId);
        Assert.Equal(snapshot.SerialNumberId, workLine.SerialNumberId);
        Assert.Equal(snapshot.SerialNumber, workLine.SerialNumber);
        Assert.Equal(snapshot.LicensePlateId, workLine.LicensePlateId);
        Assert.Equal(snapshot.InventoryStatusId, workLine.InventoryStatusId);
        Assert.Equal(snapshot.OwnerKind, workLine.OwnerKind);
        Assert.Equal(snapshot.InventoryOwnerId, workLine.InventoryOwnerId);
        Assert.Equal(snapshot.OwnerCodeSnapshot, workLine.OwnerCodeSnapshot);
        Assert.Equal(countLine.Id, int.Parse(
            workLine.SourceReference!.AsSpan("cycle-count-line:".Length),
            NumberStyles.None,
            CultureInfo.InvariantCulture));
        await ReconcileAndRecordAsync(
            "cycle-count-task-and-work-generated",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var staleTaskStart = await cycleCounts.StartTaskAsync(
            summary.TaskId,
            new CycleCountTaskStartInput(initialTask.Value.Revision + 1),
            actor.Id);
        Assert.True(staleTaskStart.IsFailure);
        Assert.Equal("cycle_count.task_revision_conflict", staleTaskStart.FirstError?.Code);
        outcomes.Add("cycle-count-task=stale-revision-rejected");
        await ReconcileAndRecordAsync(
            "cycle-count-stale-task-revision-rejected",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);

        var taskStarted = await cycleCounts.StartTaskAsync(
            summary.TaskId,
            new CycleCountTaskStartInput(initialTask.Value.Revision),
            actor.Id);
        Assert.True(taskStarted.IsSuccess, taskStarted.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.InProgress, taskStarted.Value.Status);
        outcomes.Add("cycle-count-task=started");
        await ReconcileAndRecordAsync(
            "cycle-count-task-started",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        var assigned = await workService.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput(actor.Id, null, "j130-cycle-count-assign"),
            actor.Id);
        Assert.True(assigned.IsSuccess, assigned.FirstError?.Message);
        outcomes.Add("scanner-work=assigned");
        await ReconcileAndRecordAsync(
            "cycle-count-work-assigned",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        var workStarted = await workService.StartAsync(
            workId,
            new WarehouseWorkCommandInput("j130-cycle-count-start"),
            actor.Id);
        Assert.True(workStarted.IsSuccess, workStarted.FirstError?.Message);
        outcomes.Add("scanner-work=started");
        await ReconcileAndRecordAsync(
            "cycle-count-work-started",
            context,
            reconciliation,
            checkpoints,
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
        outcomes.Add("physical-count=submitted,variance-not-applied-before-approval");
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
            checkpoints,
            CancellationToken.None);

        var approved = await cycleCounts.ApproveTaskAsync(
            summary.TaskId,
            new CycleCountTaskApprovalInput(submitted.Value.Revision, "Issue 130 verified physical count"),
            actor.Id);
        Assert.True(approved.IsSuccess, approved.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.Completed, approved.Value.Status);
        Assert.Equal(snapshot.OnHandQuantity, approved.Value.Lines.Single().ExpectedQuantity);
        Assert.Equal(countedQuantity, approved.Value.Lines.Single().CountedQuantity);
        var approvedVariance = approved.Value.Lines.Single().VarianceQuantity;
        Assert.Equal(-1m, approvedVariance);
        outcomes.Add("physical-count=approved,expected-and-counted-quantities-retained");
        await ReconcileAndRecordAsync(
            "cycle-count-variance-approved-and-reconciled",
            context,
            reconciliation,
            checkpoints,
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
        Assert.Equal(snapshot.WarehouseId, ledger.Single().WarehouseId);
        Assert.Equal(snapshot.LocationId, ledger.Single().LocationId);
        Assert.Equal(snapshot.ItemId, ledger.Single().ItemId);
        Assert.Equal(snapshot.LotId, ledger.Single().LotId);
        Assert.Equal(snapshot.SerialNumberId, ledger.Single().SerialNumberId);
        Assert.Equal(snapshot.SerialNumber, ledger.Single().SerialNumber);
        Assert.Equal(snapshot.LicensePlateId, ledger.Single().LicensePlateId);
        Assert.Equal(snapshot.InventoryStatusId, ledger.Single().InventoryStatusId);
        Assert.Equal(snapshot.BaseUnitOfMeasure, ledger.Single().BaseUnitOfMeasure);
        Assert.Equal(snapshot.OwnerKind, ledger.Single().OwnerKind);
        Assert.Equal(snapshot.InventoryOwnerId, ledger.Single().InventoryOwnerId);
        Assert.Equal(snapshot.OwnerCodeSnapshot, ledger.Single().OwnerCodeSnapshot);
        Assert.Equal(actor.Id, ledger.Single().ActorUserId);
        Assert.Equal("CycleCountLine", ledger.Single().ReferenceType);
        Assert.Equal(countLine.Id.ToString(CultureInfo.InvariantCulture), ledger.Single().ReferenceId);
        Assert.Equal(1, ledger.Single().EntrySequence);
        Assert.False(string.IsNullOrWhiteSpace(ledger.Single().CorrelationId));
        Assert.Equal($"cycle-count:{summary.TaskId}:line:{countLine.Id}:variance", ledger.Single().IdempotencyKey);
        Assert.Equal($"cycle-count:{summary.TaskId}:line:{countLine.Id}", ledger.Single().TransactionGroupId);
        var movement = await context.Movements.AsNoTracking()
            .SingleAsync(value => value.ReferenceNumber == approved.Value.TaskNumber &&
                                  value.Type == MovementType.CycleCount);
        Assert.Equal(-1m, movement.AdjustmentDelta);
        Assert.Equal(snapshot.OnHandQuantity, movement.AdjustmentBeforeQuantity);
        Assert.Equal(countedQuantity, movement.AdjustmentAfterQuantity);
        Assert.Equal(actor.Id, movement.UserId);
        Assert.Equal(snapshot.ItemId, movement.ItemId);
        Assert.Equal(snapshot.LocationId, movement.ToLocationId);
        Assert.Equal(snapshot.LotId, movement.LotId);
        Assert.Equal(snapshot.SerialNumberId, movement.SerialNumberId);
        Assert.Equal(snapshot.InventoryStatusId, movement.InventoryStatusId);
        Assert.Equal(snapshot.LicensePlateId, movement.ToLicensePlateId);
        Assert.Equal(snapshot.OwnerKind, movement.OwnerKind);
        Assert.Equal(snapshot.InventoryOwnerId, movement.InventoryOwnerId);
        Assert.Equal(snapshot.OwnerCodeSnapshot, movement.OwnerCodeSnapshot);
        Assert.Equal(movement.Id, ledger.Single().MovementId);
        var approvalAudit = await context.AuditEntries.AsNoTracking()
            .SingleAsync(value => value.Action == WmsAuditActions.CountApproved &&
                                  value.EntityId == approved.Value.TaskNumber);
        var approvalAuditCount = await context.AuditEntries.AsNoTracking()
            .CountAsync(value => value.Action == WmsAuditActions.CountApproved &&
                                 value.EntityId == approved.Value.TaskNumber);
        Assert.Equal(actor.Id, approvalAudit.ActorUserId);
        Assert.Equal(warehouseId, approvalAudit.WarehouseId);
        Assert.Equal("Desktop", approvalAudit.SourceClient);
        Assert.False(string.IsNullOrWhiteSpace(approvalAudit.CorrelationId));
        Assert.True(approvalAudit.Succeeded);
        using (var auditAfter = JsonDocument.Parse(approvalAudit.AfterJson!))
        {
            Assert.Equal(-1m, auditAfter.RootElement.GetProperty("varianceQuantity").GetDecimal());
            Assert.Equal(CycleCountTaskStatus.Completed.ToString(), auditAfter.RootElement.GetProperty("status").GetString());
        }
        outcomes.Add("approval=movement-and-ledger-linked,variance-applied-once");

        var transactionCount = await context.InventoryTransactions.CountAsync();
        var approvalReplay = await cycleCounts.ApproveTaskAsync(
            summary.TaskId,
            new CycleCountTaskApprovalInput(1, "duplicate replay"),
            actor.Id);
        Assert.True(approvalReplay.IsSuccess, approvalReplay.FirstError?.Message);
        Assert.Equal(CycleCountTaskStatus.Completed, approvalReplay.Value.Status);
        Assert.Equal(transactionCount, await context.InventoryTransactions.CountAsync());
        Assert.Equal(approvalAuditCount, await context.AuditEntries.AsNoTracking()
            .CountAsync(value => value.Action == WmsAuditActions.CountApproved &&
                                 value.EntityId == approved.Value.TaskNumber));
        await ReconcileAndRecordAsync(
            "cycle-count-approval-replayed-once",
            context,
            reconciliation,
            checkpoints,
            CancellationToken.None);
        var onHandAfterNegativeApproval = await context.InventoryBalances.AsNoTracking()
            .Where(value => value.Id == snapshot.Id)
            .Select(value => value.OnHandQuantity)
            .SingleAsync();
        Assert.Equal(countedQuantity, onHandAfterNegativeApproval);

        async Task<CycleCountTaskDto> RunAdditionalPhysicalCountAsync(
            string planKey,
            decimal physicalQuantity)
        {
            var expectedBalance = await context.InventoryBalances.AsNoTracking()
                .SingleAsync(value => value.Id == snapshot.Id);
            var additionalPlan = await cycleCounts.SavePlanAsync(
                null,
                new CycleCountPlanInput(
                    planKey,
                    warehouseId,
                    expectedBalance.LocationId,
                    expectedBalance.ItemId,
                    null,
                    1,
                    0m,
                    Blind: true,
                    CycleCountFreezePolicy.SnapshotAndReconcile,
                    new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc)),
                actor.Id);
            Assert.True(additionalPlan.IsSuccess, additionalPlan.FirstError?.Message);
            await ReconcileAndRecordAsync(
                $"{planKey}-plan-created",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);

            var additionalGeneration = await cycleCounts.GenerateAsync(
                new CycleCountGenerationQuery(warehouseId, additionalPlan.Value.Id),
                actor.Id);
            Assert.True(additionalGeneration.IsSuccess, additionalGeneration.FirstError?.Message);
            var additionalSummary = Assert.Single(additionalGeneration.Value.Tasks);
            var additionalTask = await cycleCounts.GetTaskAsync(additionalSummary.TaskId);
            Assert.True(additionalTask.IsSuccess, additionalTask.FirstError?.Message);
            var additionalCountLine = Assert.Single(additionalTask.Value.Lines);
            var additionalWorkId = Assert.IsType<int>(additionalTask.Value.WarehouseWorkId);
            var additionalWork = await workService.GetAsync(additionalWorkId);
            Assert.True(additionalWork.IsSuccess, additionalWork.FirstError?.Message);
            var additionalWorkLine = Assert.Single(additionalWork.Value.Lines);
            Assert.Equal(expectedBalance.ItemId, additionalWorkLine.ItemId);
            Assert.Equal(expectedBalance.LocationId, additionalWorkLine.SourceLocationId);
            Assert.Equal(expectedBalance.LocationId, additionalWorkLine.DestinationLocationId);
            await ReconcileAndRecordAsync(
                $"{planKey}-task-and-work-generated",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);

            var additionalStart = await cycleCounts.StartTaskAsync(
                additionalSummary.TaskId,
                new CycleCountTaskStartInput(additionalTask.Value.Revision),
                actor.Id);
            Assert.True(additionalStart.IsSuccess, additionalStart.FirstError?.Message);
            await ReconcileAndRecordAsync(
                $"{planKey}-task-started",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);
            var additionalAssigned = await workService.AssignAsync(
                additionalWorkId,
                new WarehouseWorkAssignmentInput(actor.Id, null, $"{planKey}-assign"),
                actor.Id);
            Assert.True(additionalAssigned.IsSuccess, additionalAssigned.FirstError?.Message);
            await ReconcileAndRecordAsync(
                $"{planKey}-work-assigned",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);
            var additionalWorkStarted = await workService.StartAsync(
                additionalWorkId,
                new WarehouseWorkCommandInput($"{planKey}-start"),
                actor.Id);
            Assert.True(additionalWorkStarted.IsSuccess, additionalWorkStarted.FirstError?.Message);
            await ReconcileAndRecordAsync(
                $"{planKey}-work-started",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);

            var additionalWorkCompleted = await workService.CompleteAsync(
                additionalWorkId,
                new WarehouseWorkCompletionInput(
                    $"{planKey}-complete",
                    CompletionReference: $"{planKey} physical count",
                    Scans:
                    [
                        new WarehouseWorkScanInput(
                            additionalWorkLine.Id,
                            additionalWorkLine.ItemId,
                            additionalWorkLine.SourceLocationId!.Value,
                            additionalWorkLine.DestinationLocationId!.Value,
                            physicalQuantity,
                            additionalWorkLine.LicensePlateId,
                            LotId: additionalWorkLine.LotId,
                            SerialNumberId: additionalWorkLine.SerialNumberId,
                            SerialNumber: additionalWorkLine.SerialNumber)
                    ]),
                actor.Id);
            Assert.True(additionalWorkCompleted.IsSuccess, additionalWorkCompleted.FirstError?.Message);
            Assert.Equal(WarehouseWorkStatus.Completed, additionalWorkCompleted.Value.Status);
            var physicalCount = await cycleCounts.GetTaskAsync(additionalSummary.TaskId);
            Assert.True(physicalCount.IsSuccess, physicalCount.FirstError?.Message);
            Assert.Equal(physicalQuantity, physicalCount.Value.Lines.Single().CountedQuantity);
            await ReconcileAndRecordAsync(
                $"{planKey}-physical-count-submitted",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);

            var expectedVariance = physicalQuantity - expectedBalance.OnHandQuantity;
            if (expectedVariance == 0m)
            {
                Assert.Equal(CycleCountTaskStatus.Completed, physicalCount.Value.Status);
                Assert.Equal(expectedBalance.OnHandQuantity, physicalCount.Value.Lines.Single().ExpectedQuantity);
                Assert.Equal(0m, physicalCount.Value.Lines.Single().VarianceQuantity);
                Assert.Empty(await context.Movements.AsNoTracking()
                    .Where(value => value.ReferenceNumber == physicalCount.Value.TaskNumber &&
                                    value.Type == MovementType.CycleCount)
                    .ToArrayAsync());
                Assert.Empty(await context.InventoryTransactions.AsNoTracking()
                    .Where(value => value.ReferenceType == "CycleCountLine" &&
                                    value.ReferenceId == additionalCountLine.Id.ToString(CultureInfo.InvariantCulture) &&
                                    value.Type == InventoryTransactionType.CountVariance)
                    .ToArrayAsync());
                outcomes.Add("zero-variance-count=completed-without-adjustment-ledger");
                return physicalCount.Value;
            }

            Assert.True(expectedVariance > 0m);
            Assert.Equal(CycleCountTaskStatus.AwaitingApproval, physicalCount.Value.Status);
            using var competingApprovalScope = provider.CreateScope();
            var competingCycleCounts = competingApprovalScope.ServiceProvider
                .GetRequiredService<ICycleCountService>();
            var approvalResults = await Task.WhenAll(
                cycleCounts.ApproveTaskAsync(
                    additionalSummary.TaskId,
                    new CycleCountTaskApprovalInput(
                        physicalCount.Value.Revision,
                        "Issue 130 positive variance count"),
                    actor.Id),
                competingCycleCounts.ApproveTaskAsync(
                    additionalSummary.TaskId,
                    new CycleCountTaskApprovalInput(
                        physicalCount.Value.Revision,
                        "Issue 130 concurrent positive variance approval"),
                    actor.Id));
            var successfulApprovals = approvalResults.Where(value => value.IsSuccess).ToArray();
            Assert.NotEmpty(successfulApprovals);
            Assert.All(approvalResults, result =>
                Assert.True(
                    result.IsSuccess || result.FirstError?.Code is
                        "cycle_count.task_revision_conflict" or
                        "cycle_count.snapshot_stale" or
                        "cycle_count.approval_concurrency_conflict",
                    result.FirstError?.Message));
            var positiveApproval = successfulApprovals[0];
            Assert.True(positiveApproval.IsSuccess, positiveApproval.FirstError?.Message);
            Assert.Equal(CycleCountTaskStatus.Completed, positiveApproval.Value.Status);
            Assert.Equal(expectedBalance.OnHandQuantity, positiveApproval.Value.Lines.Single().ExpectedQuantity);
            Assert.Equal(physicalQuantity, positiveApproval.Value.Lines.Single().CountedQuantity);
            Assert.Equal(expectedVariance, positiveApproval.Value.Lines.Single().VarianceQuantity);
            var positiveLedger = await context.InventoryTransactions.AsNoTracking()
                .SingleAsync(value => value.ReferenceType == "CycleCountLine" &&
                                      value.ReferenceId == additionalCountLine.Id.ToString(CultureInfo.InvariantCulture) &&
                                      value.Type == InventoryTransactionType.CountVariance);
            Assert.Equal(expectedVariance, positiveLedger.QuantityDelta);
            Assert.Equal(expectedBalance.OnHandQuantity, positiveLedger.QuantityBefore);
            Assert.Equal(physicalQuantity, positiveLedger.QuantityAfter);
            Assert.Equal(actor.Id, positiveLedger.ActorUserId);
            var positiveMovement = await context.Movements.AsNoTracking()
                .SingleAsync(value => value.ReferenceNumber == positiveApproval.Value.TaskNumber &&
                                      value.Type == MovementType.CycleCount);
            Assert.Equal(expectedVariance, positiveMovement.AdjustmentDelta);
            Assert.Equal(positiveLedger.MovementId, positiveMovement.Id);
            Assert.Equal(1, await context.AuditEntries.AsNoTracking()
                .CountAsync(value => value.Action == WmsAuditActions.CountApproved &&
                                     value.EntityId == positiveApproval.Value.TaskNumber));
            outcomes.Add("positive-variance-count=concurrent-approval-applied-once-with-linked-movement-and-ledger");
            await ReconcileAndRecordAsync(
                $"{planKey}-positive-variance-approved",
                context,
                reconciliation,
                checkpoints,
                CancellationToken.None);
            return positiveApproval.Value;
        }

        var positiveCount = await RunAdditionalPhysicalCountAsync("J130-POSITIVE-COUNT", countedQuantity + 1m);
        var zeroVarianceCount = await RunAdditionalPhysicalCountAsync("J130-ZERO-COUNT", countedQuantity + 1m);

        var finalBalance = await context.InventoryBalances.AsNoTracking()
            .SingleAsync(value => value.Id == snapshot.Id);
        var varianceMovementCount = await context.Movements.AsNoTracking()
            .CountAsync(value => value.ReferenceNumber == approved.Value.TaskNumber &&
                                 value.Type == MovementType.CycleCount);
        var varianceLedgerCount = await context.InventoryTransactions.AsNoTracking()
            .CountAsync(value => value.ReferenceType == "CycleCountLine" &&
                                 value.ReferenceId == countLine.Id.ToString(CultureInfo.InvariantCulture) &&
                                 value.Type == InventoryTransactionType.CountVariance);
        Assert.Equal(56, checkpoints.Count);
        Assert.All(checkpoints, checkpoint => Assert.Equal(0, checkpoint.IssueCount));
        Assert.Equal(1, varianceMovementCount);
        Assert.Equal(1, varianceLedgerCount);
        Assert.Equal(1m, positiveCount.Lines.Single().VarianceQuantity);
        Assert.Equal(CycleCountTaskStatus.Completed, zeroVarianceCount.Status);
        Assert.Equal(0m, zeroVarianceCount.Lines.Single().VarianceQuantity);
        outcomes.Add("approval-replay=no-duplicate-movement-or-ledger-entry");
        WriteJourneyEvidence(new PostgreSqlJourneyEvidence(
            "cycle-count-variance-approval",
            target.TargetIdentifier,
            outcomes,
            [
                "concurrent approval provider journey"
            ],
            checkpoints,
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["onHandBeforeCount"] = snapshot.OnHandQuantity,
                ["reservedBeforeCount"] = snapshot.ReservedQuantity,
                ["availableBeforeCount"] = snapshot.AvailableQuantity,
                ["countedQuantity"] = countedQuantity,
                ["varianceQuantity"] = approvedVariance
                    ?? throw new InvalidOperationException("The approved cycle-count line did not retain its variance."),
                ["onHandAfterNegativeApproval"] = onHandAfterNegativeApproval,
                ["onHandAfterPositiveVariance"] = positiveCount.Lines.Single().CountedQuantity
                    ?? throw new InvalidOperationException("The positive cycle-count line did not retain its count."),
                ["onHandAfterZeroVariance"] = finalBalance.OnHandQuantity,
                ["reservedAfterApproval"] = finalBalance.ReservedQuantity,
                ["availableAfterApproval"] = finalBalance.AvailableQuantity
            },
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["cycleCountTasks"] = await context.CycleCountTasks.CountAsync(),
                ["varianceMovements"] = await context.Movements.AsNoTracking()
                    .CountAsync(value => value.Type == MovementType.CycleCount),
                ["varianceLedgerEntries"] = await context.InventoryTransactions.AsNoTracking()
                    .CountAsync(value => value.Type == InventoryTransactionType.CountVariance)
            },
            ReconciliationClean: checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: checkpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: checkpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount)));
    }
}
