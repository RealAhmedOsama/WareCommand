using FluentAssertions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class ReceivingSessionTests
{
    private static readonly DateTime OpenedAt = new(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BlindReceipt_RequiresSupervisorOverrideAndReason()
    {
        var action = () => new ReceivingSession(
            warehouseId: 1,
            receivingLocationId: 2,
            sourceType: ReceivingSessionSourceType.BlindReceipt,
            sessionReference: "rcv-001",
            userId: "receiver-1",
            openedAtUtc: OpenedAt);

        action.Should().Throw<ArgumentException>()
            .WithMessage("*blind receiving session requires a supervisor override*");
    }

    [Fact]
    public void PausedSession_CannotAcceptScan_UntilResumed()
    {
        var session = CreateSession();
        var scan = CreateScan(session, "op-1");

        session.Pause(OpenedAt.AddMinutes(5));

        var pausedAction = () => session.AddScan(scan);
        pausedAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot accept scans*");

        session.Resume(OpenedAt.AddMinutes(10));
        session.AddScan(scan);

        session.Scans.Should().ContainSingle();
        session.Status.Should().Be(ReceivingSessionStatus.Open);
    }

    [Fact]
    public void SessionLine_UsesPreviouslyReceivedQuantityAndTolerance()
    {
        var session = CreateSession();
        var line = new ReceivingSessionLine(
            session,
            lineNumber: 1,
            itemId: 10,
            itemSkuSnapshot: "SKU-10",
            itemNameSnapshot: "Item 10",
            baseUnitOfMeasure: "EA",
            expectedBaseQuantity: 10m,
            overDeliveryTolerancePercent: 10m,
            previouslyReceivedBaseQuantity: 6m);

        line.MaximumReceivableBaseQuantity.Should().Be(11m);
        line.RemainingBaseQuantity.Should().Be(4m);
        line.EnsureCanRecordScan(4m);
        line.RecordScan(4m, OpenedAt.AddMinutes(1));

        line.IsFullyReceived.Should().BeTrue();
        line.RemainingBaseQuantity.Should().Be(0m);
        var excessiveScan = () => line.EnsureCanRecordScan(2m);
        excessiveScan.Should().Throw<InvalidOperationException>()
            .WithMessage("*exceeds the receiving-session line tolerance*");
    }

    [Fact]
    public void CompletedSession_CanBePartialOnlyWhenExplicitlyAllowed()
    {
        var session = CreateSession();
        var line = new ReceivingSessionLine(
            session,
            lineNumber: 1,
            itemId: 10,
            itemSkuSnapshot: "SKU-10",
            itemNameSnapshot: "Item 10",
            baseUnitOfMeasure: "EA",
            expectedBaseQuantity: 10m);
        session.AddLine(line);
        var scan = CreateScan(session, "op-1");
        session.AddScan(scan);
        scan.MarkCompleted(2m, movementId: 11, receiptId: 12, "RCV-1", OpenedAt.AddMinutes(1));
        line.RecordScan(2m, OpenedAt.AddMinutes(1));

        var strictAction = () => session.Complete(allowPartial: false, OpenedAt.AddMinutes(2));
        strictAction.Should().Throw<InvalidOperationException>()
            .WithMessage("*open demand*");

        session.Complete(allowPartial: true, OpenedAt.AddMinutes(3));
        session.Status.Should().Be(ReceivingSessionStatus.PartiallyCompleted);
    }

    private static ReceivingSession CreateSession() => new(
        warehouseId: 1,
        receivingLocationId: 2,
        sourceType: ReceivingSessionSourceType.DockArrival,
        sessionReference: "rcv-001",
        userId: "receiver-1",
        openedAtUtc: OpenedAt,
        dockLocationId: 3);

    private static ReceivingSessionScan CreateScan(ReceivingSession session, string operationId) => new(
        session,
        operationId,
        "SKU-10",
        "SKU-10",
        enteredQuantity: 1m,
        unitOfMeasure: "EA",
        packagingCode: null,
        lotNumber: null,
        expiryDate: null,
        serialNumber: null,
        licensePlateId: null,
        sessionLineId: null,
        userId: "receiver-1",
        requestedAtUtc: OpenedAt.AddMinutes(1));
}
