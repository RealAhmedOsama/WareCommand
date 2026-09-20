using Wms.Application.Backups;

namespace Wms.Infrastructure.Tests.Backups;

public sealed class WmsBackupHealthStateTests
{
    [Fact]
    public void EnabledStateRequiresRecentSuccessfulBackup()
    {
        var state = new WmsBackupHealthState(true, TimeSpan.FromHours(36));
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

        state.Snapshot.IsHealthy(state.MaximumAge, now).Should().BeFalse();

        state.MarkSuccess(now.AddHours(-1), "backup.wcbak", 1234);
        state.Snapshot.IsHealthy(state.MaximumAge, now).Should().BeTrue();

        state.MarkFailure(now);
        state.Snapshot.IsHealthy(state.MaximumAge, now).Should().BeFalse();
    }

    [Fact]
    public void DisabledStateIsHealthyWithoutAnArtifact()
    {
        var state = new WmsBackupHealthState(false, TimeSpan.FromHours(36));

        state.Snapshot.IsHealthy(state.MaximumAge, DateTimeOffset.UtcNow).Should().BeTrue();
    }
}
