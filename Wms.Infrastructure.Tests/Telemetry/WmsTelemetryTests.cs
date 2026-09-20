using System.Diagnostics;
using Wms.Application.Telemetry;
using Xunit;

namespace Wms.Infrastructure.Tests.Telemetry;

public sealed class WmsTelemetryTests
{
    [Fact]
    public void InventoryActivityUsesAStableNameAndOmitsPayloadTags()
    {
        Activity? started = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WmsTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = activity => started = activity
        };
        ActivitySource.AddActivityListener(listener);

        using (var operation = WmsTelemetry.BeginInventoryOperation("receipt", warehouseId: 7))
        {
            operation.Complete(3);
        }

        Assert.NotNull(started);
        Assert.Equal("wms.inventory.receipt", started!.DisplayName);
        Assert.Equal(7, started.GetTagItem("wms.warehouse.id"));
        Assert.DoesNotContain(
            started.Tags,
            tag => tag.Key.Contains("barcode", StringComparison.OrdinalIgnoreCase) ||
                tag.Key.Contains("payload", StringComparison.OrdinalIgnoreCase) ||
                tag.Key.Contains("serial", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DisabledJobStateIsHealthyButEnabledStateFailsClosedUntilRunnerRegisters()
    {
        var disabled = new WmsBackgroundJobHealthState(enabled: false);
        Assert.True(disabled.IsHealthy);

        var enabled = new WmsBackgroundJobHealthState(enabled: true);
        Assert.False(enabled.IsHealthy);
        enabled.RegisterRunner();
        Assert.True(enabled.IsHealthy);
        enabled.SetQueueBacklog(12);
        Assert.Equal(12, enabled.QueueBacklog);
        enabled.MarkUnhealthy();
        Assert.False(enabled.IsHealthy);
    }
}
