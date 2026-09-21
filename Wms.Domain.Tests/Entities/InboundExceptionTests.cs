using FluentAssertions;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class InboundExceptionTests
{
    private static readonly DateTime OccurredAt = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AssignmentAndReviewPreserveQueueStateUntilResolution()
    {
        var exception = CreateException();

        exception.Assign("owner-1", "RECEIVING", "supervisor-1", OccurredAt);
        exception.StartReview("supervisor-1", OccurredAt.AddMinutes(5));

        exception.Status.Should().Be(InboundExceptionStatus.UnderReview);
        exception.OwnerUserId.Should().Be("owner-1");
        exception.AssignedTeamCode.Should().Be("RECEIVING");
        exception.ReviewStartedByUserId.Should().Be("supervisor-1");

        exception.ApplyResolution(
            InboundExceptionResolution.CorrectSourceData,
            "Source document corrected",
            "supervisor-1",
            OccurredAt.AddMinutes(10));

        exception.Status.Should().Be(InboundExceptionStatus.Resolved);
        exception.Resolution.Should().Be(InboundExceptionResolution.CorrectSourceData);
        exception.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void HoldAndCancelAreControlledNonAvailableOutcomes()
    {
        var held = CreateException();
        held.ApplyResolution(
            InboundExceptionResolution.Hold,
            "Waiting for supplier confirmation",
            "supervisor-1",
            OccurredAt);

        held.Status.Should().Be(InboundExceptionStatus.Held);
        held.IsTerminal.Should().BeFalse();

        var cancelled = CreateException("idempotency-2");
        cancelled.Cancel("Duplicate case", "supervisor-1", OccurredAt);

        cancelled.Status.Should().Be(InboundExceptionStatus.Cancelled);
        cancelled.Resolution.Should().Be(InboundExceptionResolution.Cancel);
        cancelled.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void TerminalExceptionCannotBeAssignedAgain()
    {
        var exception = CreateException();
        exception.Cancel("Cancelled", "supervisor-1", OccurredAt);

        var action = () => exception.Assign("owner-2", null, "supervisor-1", OccurredAt);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be changed*");
    }

    private static InboundException CreateException(string key = "idempotency-1") => new(
        warehouseId: 1,
        exceptionNumber: "INB-EX-1",
        idempotencyKey: key,
        code: InboundExceptionCode.DamagedPackage,
        severity: InboundExceptionSeverity.High,
        reason: "Package damage observed",
        queueCode: "RECEIVING",
        dueAtUtc: OccurredAt.AddHours(2),
        itemId: 10,
        expectedBaseQuantity: 10m,
        actualBaseQuantity: 8m,
        varianceBaseQuantity: -2m,
        createdByUserId: "receiver-1");
}
