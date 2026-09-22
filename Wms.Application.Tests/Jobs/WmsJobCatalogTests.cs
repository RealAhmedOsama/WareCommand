using Wms.Application.Jobs;

namespace Wms.Application.Tests.Jobs;

public sealed class WmsJobCatalogTests
{
    [Fact]
    public void CatalogUsesStableNamesQueuesAndRecurringIdentifiers()
    {
        WmsJobCatalog.All.Should().HaveCount(13);
        WmsJobCatalog.All.Select(definition => definition.Name)
            .Should().OnlyHaveUniqueItems();

        WmsJobCatalog.All.Should().AllSatisfy(definition =>
        {
            WmsJobQueues.All.Should().Contain(definition.Queue);
            WmsJobCatalog.GetRecurringId(definition.Name).Should().Be(definition.Name);
            definition.Cron.Should().NotBeNullOrWhiteSpace();
            definition.ScheduleTimeZone.Should().Be(WmsJobScheduleTimeZones.Utc);
            definition.IdempotencyWindow.Should().BePositive();
            definition.Timeout.Should().BePositive();
        });
    }

    [Fact]
    public void ExecutionKeyIsStableWithinItsScheduleBucket()
    {
        var first = new DateTimeOffset(2026, 9, 20, 10, 1, 0, TimeSpan.Zero);
        var sameBucket = first.AddMinutes(10);
        var nextBucket = first.AddMinutes(15);

        WmsJobCatalog.GetExecutionKey(WmsJobNames.LowStockAlerts, first)
            .Should()
            .Be(WmsJobCatalog.GetExecutionKey(WmsJobNames.LowStockAlerts, sameBucket));
        WmsJobCatalog.GetExecutionKey(WmsJobNames.LowStockAlerts, first)
            .Should()
            .NotBe(WmsJobCatalog.GetExecutionKey(WmsJobNames.LowStockAlerts, nextBucket));
    }

    [Fact]
    public void EnvelopeNormalizesContextAndRejectsUnknownJobs()
    {
        var envelope = WmsJobEnvelope.Create(
            WmsJobNames.Cleanup,
            "  cleanup-key  ",
            "  correlation  ",
            "  actor-id  ",
            "  actor-name  ",
            warehouseId: 7,
            referenceId: "  reference  ");

        envelope.IdempotencyKey.Should().Be("cleanup-key");
        envelope.CorrelationId.Should().Be("correlation");
        envelope.ActorUserId.Should().Be("actor-id");
        envelope.ActorUserName.Should().Be("actor-name");
        envelope.WarehouseId.Should().Be(7);
        envelope.ReferenceId.Should().Be("reference");

        var act = () => WmsJobEnvelope.Create("unknown-job", "key");
        act.Should().Throw<ArgumentException>();
    }
}
