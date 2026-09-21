using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class WarehouseWorkTests
{
    [Fact]
    public void LifecycleRequiresAssignmentAndRecordsTerminalState()
    {
        var work = CreateWork();
        work.MakeAvailable(DateTime.UtcNow);
        work.Assign("worker-1", null, "supervisor-1", DateTime.UtcNow);
        work.Start("worker-1", DateTime.UtcNow);
        work.Lines.Single().RecordActualQuantity(10m);
        work.Complete("worker-1", DateTime.UtcNow);

        work.Status.Should().Be(WarehouseWorkStatus.Completed);
        work.IsTerminal.Should().BeTrue();
        work.CompletedByUserId.Should().Be("worker-1");

        var act = () => work.AddLine(new WarehouseWorkLine(2, 1, 2, 1m, "EA"));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be changed*");
    }

    [Fact]
    public void CompletionRejectsShortQuantityWithoutSupervisorOverride()
    {
        var work = StartWork();
        work.Lines.Single().RecordActualQuantity(5m);

        var act = () => work.Complete("worker-1", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*supervisor override*");
        work.Status.Should().Be(WarehouseWorkStatus.InProgress);

        work.Complete("worker-1", DateTime.UtcNow, supervisorOverride: true, overrideReason: "Approved shortage");

        work.Status.Should().Be(WarehouseWorkStatus.Completed);
        work.SupervisorOverride.Should().BeTrue();
        work.OverrideReason.Should().Be("Approved shortage");
    }

    [Fact]
    public void AssignmentAndActualQuantityEnforceOwnershipAndBounds()
    {
        var work = CreateWork();
        work.MakeAvailable(DateTime.UtcNow);
        work.Assign("worker-1", null, "supervisor-1", DateTime.UtcNow);

        var wrongWorker = () => work.Start("worker-2", DateTime.UtcNow);
        wrongWorker.Should().Throw<InvalidOperationException>()
            .WithMessage("*assigned worker*");

        var overPlan = () => work.Lines.Single().RecordActualQuantity(11m);
        overPlan.Should().Throw<InvalidOperationException>()
            .WithMessage("*planned quantity*");
    }

    [Fact]
    public void SupervisorOverrideRequiresReason()
    {
        var work = CreateWork();
        work.MakeAvailable(DateTime.UtcNow);

        var act = () => work.Assign(
            "worker-1",
            null,
            "supervisor-1",
            DateTime.UtcNow,
            supervisorOverride: true);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*override reason*");
    }

    private static WarehouseWork StartWork()
    {
        var work = CreateWork();
        work.MakeAvailable(DateTime.UtcNow);
        work.Assign("worker-1", null, "supervisor-1", DateTime.UtcNow);
        work.Start("worker-1", DateTime.UtcNow);
        return work;
    }

    private static WarehouseWork CreateWork()
    {
        var work = new WarehouseWork(
            "WORK-TEST-1",
            "create-key-1",
            WarehouseWorkType.Putaway,
            1,
            "RECEIPT",
            "receipt-1");
        work.AddLine(new WarehouseWorkLine(1, 1, 2, 10m, "EA"));
        return work;
    }
}
