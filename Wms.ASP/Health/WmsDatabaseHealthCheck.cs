using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Infrastructure.Data;

namespace Wms.ASP.Health;

public sealed class WmsDatabaseHealthCheck(
    IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("The PostgreSQL dependency is reachable.")
                : HealthCheckResult.Unhealthy("The PostgreSQL dependency is not reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "The PostgreSQL dependency health check failed.",
                exception);
        }
    }
}
