using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Inbound;
using Wms.Application.Inventory;
using Wms.Application.Purchasing;
using Wms.Application.Quality;
using Wms.Application.Receiving;
using Wms.Application.WarehouseWork;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Integration;

public sealed partial class PostgreSqlJourneyTests
{
    private static async Task<PostgreSqlJourneyEvidence> RunInboundQualityPutawayJourneyAsync(
        IServiceProvider services,
        WmsDbContext context,
        IInventoryReconciliationService reconciliation,
        string actorUserId,
        int warehouseId,
        string targetIdentifier)
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

        var item = await context.Items.AsNoTracking()
            .Where(value => value.IsActive && !value.RequiresLot && !value.RequiresSerial)
            .OrderBy(value => value.Id)
            .FirstAsync();
        var supplierId = await context.Suppliers.AsNoTracking()
            .Where(value => value.IsActive)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var receivingLocationId = await context.Locations.AsNoTracking()
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Type == LocationType.Receiving &&
                            value.IsActive &&
                            value.IsReceivable)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        var destinationLocationId = await context.Locations.AsNoTracking()
            .Where(value => value.WarehouseId == warehouseId &&
                            value.Type == LocationType.Storage &&
                            value.IsActive &&
                            value.IsPickable)
            .OrderBy(value => value.Id)
            .Select(value => value.Id)
            .FirstAsync();
        const decimal receiptQuantity = 4m;
        const string qualityReference = "J130-QA-PASS-001";
        var originalItemQuantity = await SumItemQuantityAsync(context, item.Id);
        var originalReceivingQuantity = await SumLocationQuantityAsync(
            context,
            receivingLocationId,
            item.Id);

        var qualityService = services.GetRequiredService<IQualityInspectionService>();
        var purchaseOrderService = services.GetRequiredService<IPurchaseOrderService>();
        var asnService = services.GetRequiredService<IAdvanceShippingNoticeService>();
        var receivingService = services.GetRequiredService<IReceivingExecutionService>();
        var workService = services.GetRequiredService<IWarehouseWorkService>();

        var profile = await qualityService.CreateProfileAsync(
            new QualityProfileInput(
                "J130-INBOUND-QUALITY",
                "Issue 130 inbound inspection",
                "فحص الوارد للمسار 130",
                QualityRiskLevel.High,
                QualitySamplingMethod.FullInspection,
                0m,
                WarehouseId: warehouseId,
                SupplierId: supplierId,
                ItemId: item.Id,
                Tests:
                [
                    new QualityProfileTestInput(
                        1,
                        "ACCEPTANCE",
                        "Inbound acceptance",
                        "قبول الوارد",
                        QualityMeasurementType.Boolean)
                ]),
            actorUserId);
        Assert.True(profile.IsSuccess, profile.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-quality-profile-created");

        var order = await purchaseOrderService.CreateAsync(
            new PurchaseOrderInput(
                warehouseId,
                supplierId,
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 1, 16),
                SourceType: "ISSUE-130-JOURNEY",
                SourceReference: "J130-INBOUND-001",
                Lines: [new PurchaseOrderLineInput(item.Sku, receiptQuantity)]),
            actorUserId);
        Assert.True(order.IsSuccess, order.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-purchase-order-created");
        var orderLine = Assert.Single(order.Value.Lines);
        var confirmedOrder = await purchaseOrderService.ConfirmAsync(order.Value.Id, actorUserId);
        Assert.True(confirmedOrder.IsSuccess, confirmedOrder.FirstError?.Message);
        Assert.Equal(PurchaseOrderStatus.Confirmed, confirmedOrder.Value.Status);
        await ReconcileCheckpointAsync("inbound-purchase-order-confirmed");

        var notice = await asnService.CreateAsync(
            new AdvanceShippingNoticeInput(
                warehouseId,
                supplierId,
                CarrierName: "J130 Carrier",
                ExternalReference: "J130-INBOUND-001",
                SourceType: "ISSUE-130-JOURNEY",
                Lines:
                [
                    new AdvanceShippingNoticeLineInput(
                        item.Sku,
                        receiptQuantity,
                        PurchaseOrderId: order.Value.Id,
                        PurchaseOrderLineId: orderLine.Id)
                ]),
            actorUserId);
        Assert.True(notice.IsSuccess, notice.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-asn-created");
        var noticeLine = Assert.Single(notice.Value.Lines);
        var submittedNotice = await asnService.SubmitAsync(notice.Value.Id, actorUserId);
        Assert.True(submittedNotice.IsSuccess, submittedNotice.FirstError?.Message);
        Assert.Equal(AdvanceShippingNoticeStatus.Submitted, submittedNotice.Value.Status);
        await ReconcileCheckpointAsync("inbound-asn-submitted");
        var expectedNotice = await asnService.MarkExpectedAsync(notice.Value.Id, actorUserId);
        Assert.True(expectedNotice.IsSuccess, expectedNotice.FirstError?.Message);
        Assert.Equal(AdvanceShippingNoticeStatus.Expected, expectedNotice.Value.Status);
        await ReconcileCheckpointAsync("inbound-asn-expected");
        var arrivedNotice = await asnService.ArriveAsync(notice.Value.Id, null, actorUserId);
        Assert.True(arrivedNotice.IsSuccess, arrivedNotice.FirstError?.Message);
        Assert.Equal(AdvanceShippingNoticeStatus.Arrived, arrivedNotice.Value.Status);
        await ReconcileCheckpointAsync("inbound-asn-arrived");

        var session = await receivingService.StartAsync(
            new ReceivingSessionStartInput(
                warehouseId,
                receivingLocationId,
                ReceivingSessionSourceType.AdvanceShippingNotice,
                AdvanceShippingNoticeId: notice.Value.Id,
                SessionReference: "J130-INBOUND-SESSION-001"),
            actorUserId);
        Assert.True(session.IsSuccess, session.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-receiving-session-started");
        var scanned = await receivingService.ScanAsync(
            session.Value.Id,
            new ReceivingScanInput(
                "j130-inbound-scan-001",
                item.Sku,
                ItemSku: item.Sku,
                Quantity: receiptQuantity,
                UnitOfMeasure: item.UnitOfMeasure,
                AdvanceShippingNoticeLineId: noticeLine.Id),
            actorUserId);
        Assert.True(scanned.IsSuccess, scanned.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-receiving-scan-recorded");
        var receivedSession = await receivingService.CompleteAsync(
            session.Value.Id,
            new ReceivingSessionCompletionInput(),
            actorUserId);
        Assert.True(receivedSession.IsSuccess, receivedSession.FirstError?.Message);
        Assert.Equal(ReceivingSessionStatus.Completed, receivedSession.Value.Status);
        var receivedScan = Assert.Single(receivedSession.Value.Scans);
        Assert.Equal(ReceivingScanStatus.Completed, receivedScan.Status);
        Assert.NotNull(receivedScan.ReceiptId);
        await ReconcileCheckpointAsync("inbound-receipt-completed-quality-pending");

        var receipt = await context.Receipts.AsNoTracking()
            .Include(value => value.Lines)
            .SingleAsync(value => value.Id == receivedScan.ReceiptId!.Value);
        var receiptLine = Assert.Single(receipt.Lines);
        var inspection = await context.QualityInspections.AsNoTracking()
            .Include(value => value.QualityProfile)
                .ThenInclude(value => value!.Tests)
            .SingleAsync(value => value.ReceiptLineId == receiptLine.Id);
        Assert.Equal(InventoryStatusSystemIds.QualityPending, inspection.InventoryStatusId);
        Assert.Equal(receiptQuantity, await SumLocationQuantityAsync(
            context,
            receivingLocationId,
            item.Id) - originalReceivingQuantity);
        Assert.Empty(await context.WarehouseWorks.AsNoTracking()
            .Where(value => value.SourceEntityType == "ReceiptLine" &&
                            value.SourceEntityId == receiptLine.Id.ToString(CultureInfo.InvariantCulture))
            .ToArrayAsync());
        outcomes.Add("receipt=asn-po-linked,quality-pending,no-putaway-before-inspection-pass");

        var qualityTest = inspection.QualityProfile!.Tests.Single();
        var result = await qualityService.RecordTestResultAsync(
            inspection.Id,
            new QualityInspectionTestResultInput(
                qualityTest.Id,
                inspection.SampleBaseQuantity,
                "PASS",
                NumericValue: null,
                BooleanValue: true,
                Passed: true),
            actorUserId);
        Assert.True(result.IsSuccess, result.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-quality-test-recorded");

        var disposition = await qualityService.AddDispositionAsync(
            inspection.Id,
            new QualityDispositionInput(
                QualityDispositionType.Pass,
                inspection.SampleBaseQuantity,
                "Issue 130 full inspection accepted the received stock.",
                qualityReference),
            actorUserId);
        Assert.True(disposition.IsSuccess, disposition.FirstError?.Message);
        Assert.Single(disposition.Value.Dispositions);
        Assert.Equal(QualityDispositionType.Pass, disposition.Value.Dispositions.Single().Type);
        var work = Assert.Single(await context.WarehouseWorks.AsNoTracking()
            .Include(value => value.Lines)
            .Where(value => value.SourceEntityType == "ReceiptLine" &&
                            value.SourceEntityId == receiptLine.Id.ToString(CultureInfo.InvariantCulture))
            .ToArrayAsync());
        Assert.Equal(WarehouseWorkType.Putaway, work.Type);
        Assert.Equal(receiptQuantity, work.Lines.Single().PlannedQuantity);
        Assert.Equal(InventoryStatusSystemIds.Available, work.Lines.Single().InventoryStatusId);
        var dispositionMovement = await context.Movements.AsNoTracking()
            .SingleAsync(value => value.Id == disposition.Value.Dispositions.Single().MovementId);
        Assert.Equal(MovementType.StatusChange, dispositionMovement.Type);
        Assert.Equal(InventoryStatusMovementLeg.Inbound, dispositionMovement.StatusChangeLeg);
        Assert.Equal(qualityReference, dispositionMovement.ReferenceNumber);
        await ReconcileCheckpointAsync("inbound-quality-pass-created-putaway-work");
        outcomes.Add("quality-pass=status-change-and-idempotent-receipt-putaway-work-created");

        var closedInspection = await qualityService.CloseAsync(inspection.Id, actorUserId);
        Assert.True(closedInspection.IsSuccess, closedInspection.FirstError?.Message);
        Assert.True(closedInspection.Value.IsClosed);
        await ReconcileCheckpointAsync("inbound-quality-inspection-closed");

        var putaway = await workService.GetAsync(work.Id);
        Assert.True(putaway.IsSuccess, putaway.FirstError?.Message);
        var putawayLine = Assert.Single(putaway.Value.Lines);
        var assigned = await workService.AssignAsync(
            work.Id,
            new WarehouseWorkAssignmentInput(actorUserId, null, "j130-inbound-putaway-assign"),
            actorUserId);
        Assert.True(assigned.IsSuccess, assigned.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-putaway-assigned");
        var started = await workService.StartAsync(
            work.Id,
            new WarehouseWorkCommandInput("j130-inbound-putaway-start"),
            actorUserId);
        Assert.True(started.IsSuccess, started.FirstError?.Message);
        await ReconcileCheckpointAsync("inbound-putaway-started");
        var actualDestinationLocationId = putawayLine.DestinationLocationId ?? destinationLocationId;
        var destinationQuantityBeforePutaway = await SumLocationQuantityAsync(
            context,
            actualDestinationLocationId,
            item.Id);
        var completed = await workService.CompleteAsync(
            work.Id,
            new WarehouseWorkCompletionInput(
                "j130-inbound-putaway-complete",
                CompletionReference: putaway.Value.WorkNumber,
                Scans:
                [
                    new WarehouseWorkScanInput(
                        putawayLine.Id,
                        putawayLine.ItemId,
                        putawayLine.SourceLocationId!.Value,
                        actualDestinationLocationId,
                        putawayLine.PlannedQuantity,
                        putawayLine.LicensePlateId,
                        LotId: putawayLine.LotId,
                        SerialNumberId: putawayLine.SerialNumberId,
                        SerialNumber: putawayLine.SerialNumber)
                ]),
            actorUserId);
        Assert.True(completed.IsSuccess, completed.FirstError?.Message);
        Assert.Equal(WarehouseWorkStatus.Completed, completed.Value.Status);
        await ReconcileCheckpointAsync("inbound-putaway-completed");

        var finalItemQuantity = await SumItemQuantityAsync(context, item.Id);
        var finalReceivingQuantity = await SumLocationQuantityAsync(
            context,
            receivingLocationId,
            item.Id);
        var finalDestinationQuantity = await SumLocationQuantityAsync(
            context,
            actualDestinationLocationId,
            item.Id);
        Assert.Equal(originalItemQuantity + receiptQuantity, finalItemQuantity);
        Assert.Equal(originalReceivingQuantity, finalReceivingQuantity);
        Assert.Equal(destinationQuantityBeforePutaway + receiptQuantity, finalDestinationQuantity);

        var finalOrder = await purchaseOrderService.GetAsync(order.Value.Id);
        Assert.True(finalOrder.IsSuccess, finalOrder.FirstError?.Message);
        Assert.Equal(receiptQuantity, Assert.Single(finalOrder.Value.Lines).ReceivedBaseQuantity);
        var finalNotice = await asnService.GetAsync(notice.Value.Id);
        Assert.True(finalNotice.IsSuccess, finalNotice.FirstError?.Message);
        Assert.Equal(receiptQuantity, Assert.Single(finalNotice.Value.Lines).ReceivedBaseQuantity);
        var putawayLedger = await context.InventoryTransactions.AsNoTracking()
            .Where(value => value.ReferenceId == putaway.Value.WorkNumber)
            .OrderBy(value => value.TransactionGroupId)
            .ThenBy(value => value.EntrySequence)
            .ToArrayAsync();
        Assert.Equal(2, putawayLedger.Length);
        Assert.All(putawayLedger, entry =>
        {
            Assert.Equal(InventoryTransactionType.Putaway, entry.Type);
            Assert.Equal(actorUserId, entry.ActorUserId);
            Assert.Equal("Movement", entry.ReferenceType);
            Assert.Equal(InventoryOwnerKind.CompanyOwned, entry.OwnerKind);
        });
        var putawayMovements = await context.Movements.AsNoTracking()
            .Where(value => value.ReferenceNumber == putaway.Value.WorkNumber &&
                            value.Type == MovementType.Putaway)
            .ToArrayAsync();
        Assert.Single(putawayMovements);
        Assert.Equal(actorUserId, putawayMovements[0].UserId);
        Assert.Equal(putawayLine.SourceLocationId, putawayMovements[0].FromLocationId);
        Assert.Equal(actualDestinationLocationId, putawayMovements[0].ToLocationId);
        var workCommands = await context.WarehouseWorkCommands.AsNoTracking()
            .Where(value => value.WarehouseWorkId == work.Id)
            .ToArrayAsync();
        Assert.All(workCommands, command => Assert.Equal(actorUserId, command.UserId));
        outcomes.Add("putaway=source-decrement,destination-increment,item-quantity-preserved,actor-ledger-qualified");

        return new PostgreSqlJourneyEvidence(
            "purchase-order-asn-quality-putaway",
            targetIdentifier,
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
                ["itemQuantityBefore"] = originalItemQuantity,
                ["itemQuantityAfterPutaway"] = finalItemQuantity,
                ["receivingQuantityBefore"] = originalReceivingQuantity,
                ["receivingQuantityAfterPutaway"] = finalReceivingQuantity,
                ["destinationQuantityBeforePutaway"] = destinationQuantityBeforePutaway,
                ["destinationQuantityAfterPutaway"] = finalDestinationQuantity,
                ["receivedQuantity"] = receiptQuantity
            },
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["purchaseOrderId"] = order.Value.Id,
                ["advanceShippingNoticeId"] = notice.Value.Id,
                ["receiptId"] = receipt.Id,
                ["receiptLineId"] = receiptLine.Id,
                ["inspectionId"] = inspection.Id,
                ["putawayWorkId"] = work.Id,
                ["putawayLedgerEntries"] = putawayLedger.Length,
                ["putawayMovements"] = putawayMovements.Length,
                ["workCommands"] = workCommands.Length
            },
            ReconciliationClean: checkpoints.All(value => value.ExpectedClean == (value.IssueCount == 0)),
            UnexpectedReconciliationIssueCount: checkpoints
                .Where(value => value.ExpectedClean)
                .Sum(value => value.IssueCount),
            ExpectedReconciliationIssueCount: checkpoints
                .Where(value => !value.ExpectedClean)
                .Sum(value => value.IssueCount));
    }
}
