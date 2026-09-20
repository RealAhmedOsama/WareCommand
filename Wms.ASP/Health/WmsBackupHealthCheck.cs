using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Application.Backups;
using Wms.Application.Context;

namespace Wms.ASP.Health;

public sealed class WmsBackupHealthCheck(
    WmsBackupHealthState state,
    IClock clock) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = state.Snapshot;
        if (snapshot.IsHealthy(state.MaximumAge, clock.UtcNow))
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                snapshot.Enabled
                    ? "The configured backup service has a recent verified backup."
                    : "Automated backups are disabled for this host."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            "The configured backup service has no recent verified backup or has consecutive failures."));
    }
}
