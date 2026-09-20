namespace Wms.Application.Telemetry;

/// <summary>
/// Shared state contract for the future durable job runner. The current host keeps
/// this disabled until issue #21 registers a real runner and storage adapter.
/// </summary>
public sealed class WmsBackgroundJobHealthState(bool enabled)
{
    private int _healthy = enabled ? 0 : 1;
    private int _runnerRegistered;
    private long _queueBacklog;

    public bool IsEnabled { get; } = enabled;

    public bool RunnerRegistered => Volatile.Read(ref _runnerRegistered) == 1;

    public bool IsHealthy => !IsEnabled ||
        (RunnerRegistered && Volatile.Read(ref _healthy) == 1);

    public long QueueBacklog => Volatile.Read(ref _queueBacklog);

    public void RegisterRunner()
    {
        if (!IsEnabled)
        {
            return;
        }

        Volatile.Write(ref _runnerRegistered, 1);
        MarkHealthy();
    }

    public void MarkHealthy()
    {
        if (IsEnabled)
        {
            Volatile.Write(ref _healthy, 1);
        }
    }

    public void MarkUnhealthy()
    {
        if (IsEnabled)
        {
            Volatile.Write(ref _healthy, 0);
        }
    }

    public void SetQueueBacklog(long backlog)
    {
        var boundedBacklog = Math.Max(0, backlog);
        Interlocked.Exchange(ref _queueBacklog, boundedBacklog);
        WmsTelemetry.SetJobBacklog(boundedBacklog);
    }

    public void RecordFailure(string jobKind = "unknown")
    {
        if (IsEnabled)
        {
            WmsTelemetry.RecordJobFailure(jobKind);
        }
    }
}
