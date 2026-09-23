using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Integration;

public sealed class PostgreSqlIntegrationTests
{
    [PostgreSqlFact]
    public async Task MigrationAndCoreQueriesWorkAgainstPostgreSql()
    {
        var connectionString = Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION");

        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var context = new WmsDbContext(options);
        Assert.Contains("Npgsql", context.Database.ProviderName, StringComparison.Ordinal);

        var initializer = new WmsDatabaseInitializer(
            context,
            new WmsDatabaseOptions(WmsDatabaseProvider.PostgreSql),
            new WmsSeedService(context),
            NullLogger<WmsDatabaseInitializer>.Instance);

        var pendingSchemaException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => initializer.InitializeAsync(WmsSeedProfile.Demo));
        Assert.Contains("not current", pendingSchemaException.Message, StringComparison.OrdinalIgnoreCase);

        await context.Database.MigrateAsync();
        await initializer.InitializeAsync(WmsSeedProfile.Demo);

        Assert.Equal(6, await context.Items.CountAsync());
        Assert.Equal(6, await context.Locations.CountAsync());

        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PG-{token}", "PostgreSQL Test Warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var location = new Location($"PG-{token}", "PostgreSQL Test Location", warehouse.Id);
        var item = new Item($"PG-{token}", "PostgreSQL Test Item", "EA");
        context.Locations.Add(location);
        context.Items.Add(item);
        await context.SaveChangesAsync();

        var repository = new ItemRepository(context);
        var result = await repository.SearchAsync(token);

        var found = Assert.Single(result);
        Assert.Equal(item.Sku, found.Sku);
        Assert.Equal(DateTimeKind.Utc, found.CreatedAt.Kind);

        var recommendationKey = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var recommendation = new GovernedRecommendation(
            recommendationKey,
            0,
            warehouse.Id,
            $"replenishment-plan:{token}",
            now.AddMinutes(-1),
            now,
            new string('a', 64),
            "deterministic-wms",
            "rules-v1",
            "inventory-replenishment-policy/v1",
            1m,
            "Deterministic replenishment proposal.",
            "{\"actionType\":\"replenishment-policy\",\"targetReference\":\"1\"}",
            "{}",
            0m,
            1m,
            now,
            now.AddDays(7),
            shadowMode: true,
            shadowComparison: "deterministic-baseline-matched",
            createdByUserId: "postgres-integration-test");
        context.GovernedRecommendations.Add(recommendation);
        await context.SaveChangesAsync();
        context.GovernedRecommendationEvents.Add(new GovernedRecommendationEvent(
            recommendation.Id,
            $"created:{recommendationKey}",
            "created",
            "postgres-integration-test",
            now,
            recommendation.Revision,
            stateFingerprint: recommendation.SourceStateFingerprint));
        await context.SaveChangesAsync();

        await using var restartedContext = new WmsDbContext(options);
        var persisted = await restartedContext.GovernedRecommendations.AsNoTracking()
            .SingleAsync(row => row.RecommendationId == recommendationKey);
        Assert.Equal(0, persisted.Status);
        Assert.True(persisted.ShadowMode);
        Assert.False(persisted.CanMutateInventory);
        Assert.Single(await restartedContext.GovernedRecommendationEvents.AsNoTracking()
            .Where(entry => entry.RecommendationId == persisted.Id)
            .ToArrayAsync());
    }
}

public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Set WARECOMMAND_TEST_POSTGRES_CONNECTION or run scripts/verify-postgresql.ps1.";
        }
    }
}
