using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Inventory;
using Wms.Application.Jobs;
using Wms.Infrastructure.Jobs;

namespace Wms.Infrastructure.Tests.Jobs;

public sealed class InventoryReconciliationJobTests
{
    private readonly Mock<IInventoryReconciliationService> _reconciliation = new();
    private readonly Mock<IWmsJobExecutionStore> _executionStore = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task LightweightHealthCheckUsesShallowReportAndPersistsIssueNotification()
    {
        var report = CreateReport(deep: false, clean: false);
        _reconciliation
            .Setup(service => service.ReconcileAsync(
                It.Is<InventoryReconciliationQuery>(query =>
                    !query.Deep && query.BatchSize == 250 && query.MaxIssues == 100),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(report));
        var job = new WmsInventoryHealthCheckJob(
            _reconciliation.Object,
            _executionStore.Object,
            _clock,
            NullLogger<WmsInventoryHealthCheckJob>.Instance);

        var result = await job.ExecuteAsync(CreateContext(WmsJobNames.InventoryHealthCheck));

        result.ItemsCreated.Should().Be(1);
        _executionStore.Verify(store => store.UpsertNotificationAsync(
                It.Is<WmsJobNotification>(notification =>
                    notification.Kind == "inventory.reconciliation" &&
                    notification.Severity == "critical"),
                _clock.UtcNow,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeepReconciliationJobUsesDeepReportAndDoesNotNotifyWhenClean()
    {
        var report = CreateReport(deep: true, clean: true);
        _reconciliation
            .Setup(service => service.ReconcileAsync(
                It.Is<InventoryReconciliationQuery>(query =>
                    query.Deep && query.BatchSize == 500 && query.MaxIssues == 2_000),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(report));
        var job = new WmsInventoryReconciliationJob(
            _reconciliation.Object,
            _executionStore.Object,
            _clock,
            NullLogger<WmsInventoryReconciliationJob>.Instance);

        var result = await job.ExecuteAsync(CreateContext(WmsJobNames.InventoryReconciliation));

        result.ItemsCreated.Should().Be(0);
        _executionStore.Verify(store => store.UpsertNotificationAsync(
                It.IsAny<WmsJobNotification>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private WmsJobContext CreateContext(string jobName) =>
        new(
            WmsJobEnvelope.Create(jobName, $"{jobName}:key", "reconcile-correlation"),
            1,
            _clock.UtcNow);

    private static InventoryReconciliationReportDto CreateReport(bool deep, bool clean) =>
        new(
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 20, 12, 0, 1, TimeSpan.Zero),
            deep,
            clean,
            false,
            clean ? 0 : 1,
            clean ? 0 : 1,
            0,
            1,
            1,
            1,
            deep ? 1 : 0,
            deep ? 1 : 0,
            deep ? 1 : 0,
            deep ? 1 : 0,
            deep ? 1 : 0,
            []);

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
