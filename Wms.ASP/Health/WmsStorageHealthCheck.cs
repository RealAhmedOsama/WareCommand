using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Application.Telemetry;

namespace Wms.ASP.Health;

public sealed class WmsStorageHealthCheck(WmsHealthOptions options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(options.StoragePath))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "The configured application storage path is unavailable."));
            }

            var root = Path.GetPathRoot(options.StoragePath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "The configured application storage volume is unavailable."));
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "The application storage volume is not ready."));
            }

            WmsTelemetry.SetStorageFreeBytes(drive.AvailableFreeSpace);
            if (drive.AvailableFreeSpace < options.MinimumFreeBytes)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "The application storage volume has insufficient free space."));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "The configured application storage path is available."));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "The configured application storage path could not be checked."));
        }
    }
}
