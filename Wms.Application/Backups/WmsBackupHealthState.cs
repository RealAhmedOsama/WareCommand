namespace Wms.Application.Backups;

public sealed record WmsBackupHealthSnapshot(
    bool Enabled,
    DateTimeOffset? LastSuccessUtc,
    DateTimeOffset? LastFailureUtc,
    string? LastArtifactName,
    long LastArtifactSizeBytes,
    int ConsecutiveFailureCount)
{
    public bool IsHealthy(TimeSpan maximumAge, DateTimeOffset nowUtc) =>
        !Enabled ||
        LastSuccessUtc.HasValue &&
        LastSuccessUtc.Value <= nowUtc &&
        nowUtc - LastSuccessUtc.Value <= maximumAge &&
        ConsecutiveFailureCount == 0;
}

public sealed class WmsBackupHealthState(
    bool enabled,
    TimeSpan maximumAge)
{
    private readonly object _sync = new();
    private DateTimeOffset? _lastSuccessUtc;
    private DateTimeOffset? _lastFailureUtc;
    private string? _lastArtifactName;
    private long _lastArtifactSizeBytes;
    private int _consecutiveFailureCount;

    public bool IsEnabled { get; } = enabled;

    public TimeSpan MaximumAge { get; } = maximumAge;

    public WmsBackupHealthSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new WmsBackupHealthSnapshot(
                    IsEnabled,
                    _lastSuccessUtc,
                    _lastFailureUtc,
                    _lastArtifactName,
                    _lastArtifactSizeBytes,
                    _consecutiveFailureCount);
            }
        }
    }

    public void MarkSuccess(
        DateTimeOffset occurredAtUtc,
        string artifactName,
        long sizeBytes)
    {
        lock (_sync)
        {
            _lastSuccessUtc = occurredAtUtc;
            _lastArtifactName = artifactName;
            _lastArtifactSizeBytes = Math.Max(0, sizeBytes);
            _consecutiveFailureCount = 0;
        }
    }

    public void MarkFailure(DateTimeOffset occurredAtUtc)
    {
        lock (_sync)
        {
            _lastFailureUtc = occurredAtUtc;
            _consecutiveFailureCount++;
        }
    }
}
