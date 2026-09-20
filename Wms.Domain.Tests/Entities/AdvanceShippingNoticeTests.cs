using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class AdvanceShippingNoticeTests
{
    [Fact]
    public void LifecycleRequiresExpectedCheckInBeforeReceivingAndCompletesWithinTolerance()
    {
        var notice = CreateNotice(underTolerance: 20m);
        notice.ReplaceDraftLines([CreateLine(underTolerance: 20m)]);

        notice.Submit("user-1", DateTime.UtcNow);
        notice.MarkExpected(DateTime.UtcNow);
        notice.Arrive("receiver-1", DateTime.UtcNow);
        notice.RecordReceipt(notice.Lines.Single().Id, 8m, DateTime.UtcNow);

        notice.Status.Should().Be(AdvanceShippingNoticeStatus.Receiving);
        notice.Lines.Single().ReceivedBaseQuantity.Should().Be(8m);

        notice.Complete("receiver-1", DateTime.UtcNow);

        notice.Status.Should().Be(AdvanceShippingNoticeStatus.Completed);
        notice.Lines.Single().IsClosed.Should().BeTrue();
        notice.CanReceive.Should().BeFalse();
    }

    [Fact]
    public void ReceiptRejectsOverToleranceAndCancellationAfterReceiptHistory()
    {
        var notice = CreateNotice(overTolerance: 10m);
        notice.ReplaceDraftLines([CreateLine(overTolerance: 10m)]);
        notice.Submit("user-1", DateTime.UtcNow);
        notice.MarkExpected(DateTime.UtcNow);
        notice.Arrive("receiver-1", DateTime.UtcNow);

        var over = () => notice.RecordReceipt(notice.Lines.Single().Id, 11.1m, DateTime.UtcNow);
        over.Should().Throw<InvalidOperationException>();

        notice.RecordReceipt(notice.Lines.Single().Id, 1m, DateTime.UtcNow);
        var cancel = () => notice.Cancel("user-1", DateTime.UtcNow);
        cancel.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DraftCanBeCancelledButCompletedNoticeCannotBeCancelled()
    {
        var draft = CreateNotice();
        draft.Cancel("user-1", DateTime.UtcNow);
        draft.Status.Should().Be(AdvanceShippingNoticeStatus.Cancelled);

        var completed = CreateNotice();
        completed.ReplaceDraftLines([CreateLine()]);
        completed.Submit("user-1", DateTime.UtcNow);
        completed.MarkExpected(DateTime.UtcNow);
        completed.Arrive("receiver-1", DateTime.UtcNow);
        completed.RecordReceipt(completed.Lines.Single().Id, 10m, DateTime.UtcNow);
        completed.Complete("receiver-1", DateTime.UtcNow);

        var cancel = () => completed.Cancel("user-1", DateTime.UtcNow);
        cancel.Should().Throw<InvalidOperationException>();
    }

    private static AdvanceShippingNotice CreateNotice(
        decimal overTolerance = 0m,
        decimal underTolerance = 0m) => new(
        "ASN-TEST-000001",
        1,
        "WH-1",
        1,
        "SUP-1",
        "Supplier",
        expectedArrivalFromUtc: DateTime.UtcNow,
        expectedArrivalToUtc: DateTime.UtcNow.AddHours(2),
        createdByUserId: "user-1");

    private static AdvanceShippingNoticeLine CreateLine(
        decimal overTolerance = 0m,
        decimal underTolerance = 0m) => new(
        1,
        1,
        "ITEM-1",
        "Item",
        "EA",
        10m,
        "EA",
        10m,
        1m,
        0,
        QuantityRoundingMode.Reject,
        0m,
        "EA -> EA",
        string.Empty,
        overTolerance,
        underTolerance);
}
