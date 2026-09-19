using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;

namespace Wms.Infrastructure.Tests.Database;

public sealed class WmsDatabaseInitializerTests
{
    [Fact]
    public async Task NoneProfile_DoesNotCreateDemoRows()
    {
        await using var database = await CreateDatabaseAsync();
        var initializer = CreateInitializer(database.Context);

        await initializer.InitializeAsync(WmsSeedProfile.None);

        Assert.Equal(0, await database.Context.Warehouses.CountAsync());
        Assert.Equal(0, await database.Context.Locations.CountAsync());
        Assert.Equal(0, await database.Context.Items.CountAsync());
        Assert.Equal(0, await database.Context.Stock.CountAsync());
    }

    [Fact]
    public async Task ReferenceProfile_SeedsOnlyMinimalReferenceData()
    {
        await using var database = await CreateDatabaseAsync();
        var initializer = CreateInitializer(database.Context);

        await initializer.InitializeAsync(WmsSeedProfile.Reference);

        Assert.Equal(1, await database.Context.Warehouses.CountAsync());
        Assert.Equal(2, await database.Context.Locations.CountAsync());
        Assert.Equal(0, await database.Context.Items.CountAsync());
        Assert.Equal(0, await database.Context.Stock.CountAsync());
    }

    [Fact]
    public async Task DemoProfile_IsSharedAcrossHostsAndIdempotent()
    {
        await using var database = await CreateDatabaseAsync();
        var initializer = CreateInitializer(database.Context);

        await initializer.InitializeAsync(WmsSeedProfile.Demo);
        var first = await ReadCountsAsync(database.Context);

        await initializer.InitializeAsync(WmsSeedProfile.Demo);
        var second = await ReadCountsAsync(database.Context);

        Assert.Equal(first, second);
        Assert.Equal(1, first.Warehouses);
        Assert.Equal(6, first.Locations);
        Assert.Equal(6, first.Items);
        Assert.Equal(6, first.Barcodes);
        Assert.Equal(4, first.Stock);
        Assert.Equal("MAIN", (await database.Context.Warehouses.SingleAsync()).Code);
        Assert.Equal(
            "MAIN",
            (await database.Context.Locations.SingleAsync(location => location.Code == "RECEIVE"))
            .Warehouse.Code);
    }

    [Fact]
    public void SeedProfileResolver_DisablesDemoDataByDefaultOutsideDevelopment()
    {
        Assert.Equal(WmsSeedProfile.None, WmsSeedProfileResolver.Resolve(null, isDevelopment: false));
        Assert.Equal(WmsSeedProfile.Demo, WmsSeedProfileResolver.Resolve(null, isDevelopment: true));
        Assert.Equal(WmsSeedProfile.Demo, WmsSeedProfileResolver.Resolve("demo", isDevelopment: false));
        Assert.Equal(WmsSeedProfile.Reference, WmsSeedProfileResolver.Resolve("Reference", isDevelopment: false));
        Assert.Throws<InvalidOperationException>(() => WmsSeedProfileResolver.Resolve("invalid", false));
    }

    private static WmsDatabaseInitializer CreateInitializer(WmsDbContext context)
    {
        return new WmsDatabaseInitializer(
            context,
            new WmsDatabaseOptions(WmsDatabaseProvider.Sqlite),
            new WmsSeedService(context),
            NullLogger<WmsDatabaseInitializer>.Instance);
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new WmsDbContext(options);
        return new TestDatabase(context, connection);
    }

    private static async Task<SeedCounts> ReadCountsAsync(WmsDbContext context)
    {
        var items = await context.Items.Include(item => item.Barcodes).ToListAsync();
        return new SeedCounts(
            await context.Warehouses.CountAsync(),
            await context.Locations.CountAsync(),
            items.Count,
            items.SelectMany(item => item.Barcodes).Count(),
            await context.Stock.CountAsync());
    }

    private sealed record SeedCounts(int Warehouses, int Locations, int Items, int Barcodes, int Stock);

    private sealed class TestDatabase(WmsDbContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public WmsDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
