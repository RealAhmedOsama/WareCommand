using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.Integration;

[Collection(PostgreSqlTestFixture.Name)]
public sealed class PostgreSqlIntegrationTests_Harness(PostgreSqlTestDatabase database)
{
    [PostgreSqlFact]
    public async Task FreshIsolatedSchemaHasAllMigrationsApplied()
    {
        await using var context = database.CreateContext();

        Assert.Contains("Npgsql", context.Database.ProviderName, StringComparison.Ordinal);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.True(await context.Database.CanConnectAsync());
        var migrations = context.Database.GetMigrations().ToArray();
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(migrations.Length, appliedMigrations.Length);
    }

    [PostgreSqlFact]
    public async Task ConstraintsPrecisionUtcAndRollbackAreEnforced()
    {
        await using var context = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGH-{token}", "PostgreSQL Harness Warehouse");
        var item = new Item($"PGH-{token}", "PostgreSQL Harness Item", "EA");
        context.AddRange(warehouse, item);
        await context.SaveChangesAsync();

        var duplicateWarehouse = new Warehouse(warehouse.Code, "Duplicate Warehouse");
        context.Warehouses.Add(duplicateWarehouse);
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var work = new WarehouseWorkEntity(
            $"WORK-PGH-{token}",
            $"create-{token}",
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            token);
        work.AddLine(new WarehouseWorkLine(1, warehouse.Id, item.Id, 12.345678901234m, "EA"));
        work.MakeAvailable(DateTime.UtcNow);
        context.WarehouseWorks.Add(work);
        await context.SaveChangesAsync();

        work.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        work.Lines.Single().PlannedQuantity.Should().Be(12.345678901234m);

        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Warehouses.Add(new Warehouse($"PG-RB-{token}", "Rollback Warehouse"));
        await context.SaveChangesAsync();
        await transaction.RollbackAsync();
        context.ChangeTracker.Clear();

        (await context.Warehouses.AnyAsync(value => value.Code == $"PG-RB-{token}"))
            .Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task RevisionTokenRejectsConcurrentWorkMutation()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var warehouse = new Warehouse($"PGC-{token}", "Concurrency Warehouse");
        var item = new Item($"PGC-{token}", "Concurrency Item", "EA");
        seedContext.AddRange(warehouse, item);
        await seedContext.SaveChangesAsync();

        var work = new WarehouseWorkEntity(
            $"WORK-PGC-{token}",
            $"create-{token}",
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            token);
        work.AddLine(new WarehouseWorkLine(1, warehouse.Id, item.Id, 1m, "EA"));
        work.MakeAvailable(DateTime.UtcNow);
        seedContext.WarehouseWorks.Add(work);
        await seedContext.SaveChangesAsync();

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = await firstContext.WarehouseWorks.SingleAsync(value => value.Id == work.Id);
        var second = await secondContext.WarehouseWorks.SingleAsync(value => value.Id == work.Id);

        first.Assign("worker-a", null, "manager", DateTime.UtcNow);
        second.Assign("worker-b", null, "manager", DateTime.UtcNow);
        await firstContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => secondContext.SaveChangesAsync());
        Assert.IsType<DbUpdateConcurrencyException>(exception.InnerException);
    }

    [PostgreSqlFact]
    public async Task LocationCodesAreWarehouseScopedAndDatabaseUnique()
    {
        await using var seedContext = database.CreateContext();
        var token = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var firstWarehouse = new Warehouse($"PGL-{token}", "Location uniqueness warehouse");
        var secondWarehouse = new Warehouse($"PGL2-{token}", "Second location uniqueness warehouse");
        seedContext.AddRange(firstWarehouse, secondWarehouse);
        await seedContext.SaveChangesAsync();

        await using var firstContext = database.CreateContext();
        await using var duplicateContext = database.CreateContext();
        await using var secondWarehouseContext = database.CreateContext();

        firstContext.Locations.Add(new Location($"LOC-{token}", "First location", firstWarehouse.Id));
        await firstContext.SaveChangesAsync();

        duplicateContext.Locations.Add(new Location($" loc-{token} ", "Duplicate location", firstWarehouse.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());

        secondWarehouseContext.Locations.Add(new Location($"LOC-{token}", "Second warehouse location", secondWarehouse.Id));
        await secondWarehouseContext.SaveChangesAsync();

        Assert.Equal(
            2,
            await seedContext.Locations
                .Where(location => location.Code == $"LOC-{token}")
                .CountAsync());
    }
}
