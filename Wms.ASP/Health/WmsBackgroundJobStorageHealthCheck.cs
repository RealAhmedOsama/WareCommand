using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Application.Telemetry;

namespace Wms.ASP.Health;

public sealed class WmsBackgroundJobStorageHealthCheck(
    WmsBackgroundJobHealthState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!state.IsEnabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Background jobs are disabled for this host."));
        }

        if (!state.RunnerRegistered || !state.IsHealthy)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The configured background-job runner or storage is unavailable."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "The configured background-job runner and storage are healthy."));
    }
}
