using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Wms.ASP.Health;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Xunit;

namespace Wms.ASP.Tests.Health;

public sealed class WmsDatabaseHealthCheckTests
{
    [Fact]
    public async Task DatabaseHealthRecoversAfterAnUnavailableStoragePath()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "warecommand-health-" + Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "database", "health.db");
        ServiceProvider? provider = null;
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<WmsDbContext>(options => options.UseSqlite(
                $"Data Source={databasePath};Pooling=False"));
            provider = services.BuildServiceProvider();
            var check = new WmsDatabaseHealthCheck(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new WmsDatabaseOptions(WmsDatabaseProvider.Sqlite));

            var unavailable = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Unhealthy, unavailable.Status);

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider
                    .GetRequiredService<WmsDbContext>()
                    .Database
                    .EnsureCreatedAsync();
            }

            var recovered = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.True(
                recovered.Status == HealthStatus.Healthy,
                recovered.Description);
        }
        finally
        {
            if (provider is not null)
            {
                await provider.DisposeAsync();
            }

            if (Directory.Exists(root))
            {
                for (var attempt = 0; attempt < 10 && Directory.Exists(root); attempt++)
                {
                    try
                    {
                        Directory.Delete(root, recursive: true);
                    }
                    catch (IOException) when (attempt < 9)
                    {
                        await Task.Delay(100);
                    }
                }
            }
        }
    }
}
