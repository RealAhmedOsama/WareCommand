using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Wms.ASP.Health;

public sealed class WmsApplicationReadinessHealthCheck(
    WmsApplicationHealthState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsReady
            ? HealthCheckResult.Healthy("The application startup checks completed.")
            : HealthCheckResult.Unhealthy("The application startup checks are incomplete."));
}
