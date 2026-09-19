using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Database;

public sealed class WmsDatabaseInitializer(
    WmsDbContext context,
    WmsDatabaseOptions databaseOptions,
    IWmsSeedService seedService,
    ILogger<WmsDatabaseInitializer> logger) : IWmsDatabaseInitializer
{
    public async Task InitializeAsync(
        WmsSeedProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (databaseOptions.Provider == WmsDatabaseProvider.PostgreSql)
            {
                var pendingMigrations = (await context.Database
                        .GetPendingMigrationsAsync(cancellationToken))
                    .ToArray();
                if (pendingMigrations.Length > 0)
                {
                    throw new InvalidOperationException(
                        "The PostgreSQL schema is not current. Apply checked-in EF migrations before starting WareCommand. " +
                        $"Pending migrations: {string.Join(", ", pendingMigrations)}.");
                }
            }
            else
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }

            await seedService.SeedAsync(profile, cancellationToken);

            logger.LogInformation("Database initialized successfully using {SeedProfile} seed profile", profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database using {SeedProfile}", profile);
            throw;
        }
    }

}
