using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;

namespace Wms.ASP.Health;

public sealed class WmsDatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    WmsDatabaseOptions databaseOptions) : IHealthCheck
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
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy(
                    "The configured database dependency is not reachable.");
            }

            if (databaseOptions.Provider == WmsDatabaseProvider.PostgreSql &&
                (await dbContext.Database
                    .GetPendingMigrationsAsync(cancellationToken))
                .Any())
            {
                return HealthCheckResult.Unhealthy(
                    "The configured database schema is not current.");
            }

            return HealthCheckResult.Healthy("The configured database dependency is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy(
                "The configured database dependency health check failed.");
        }
    }
}
