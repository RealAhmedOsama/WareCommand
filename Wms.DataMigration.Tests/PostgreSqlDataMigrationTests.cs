using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wms.DataMigration;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.DataMigration.Tests;

public sealed class PostgreSqlDataMigrationTests
{
    [DataMigrationFact]
    public async Task ApplyReconcileAndRollbackPreserveSourceAndRelationships()
    {
        var targetConnectionString = Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION")!;
        var root = Path.Combine(Path.GetTempPath(), $"warecommand-migration-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "warehouse.db");
        var invalidSourcePath = Path.Combine(root, "warehouse-invalid.db");
        var backupDirectory = Path.Combine(root, "backups");

        try
        {
            var sourceIds = await CreateRepresentativeSourceAsync(sourcePath);
            var sourceHashBefore = ComputeHash(sourcePath);

            var targetOptions = new DbContextOptionsBuilder<WmsDbContext>()
                .UseNpgsql(
                    targetConnectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName))
                .Options;
            await using (var targetContext = new WmsDbContext(targetOptions))
            {
                await targetContext.Database.MigrateAsync();
            }

            var dryRunReportPath = Path.Combine(root, "dry-run-report.json");
            var dryRunExitCode = await Program.Main(
            [
                "--source", sourcePath,
                "--target", targetConnectionString,
                "--report", dryRunReportPath
            ]);

            Assert.Equal(0, dryRunExitCode);
            Assert.True(File.Exists(dryRunReportPath));
            using var dryRunDocument = JsonDocument.Parse(await File.ReadAllTextAsync(dryRunReportPath));
            Assert.Equal("DryRun", dryRunDocument.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, dryRunDocument.RootElement.GetProperty("source").GetProperty("rowCounts").GetProperty("Items").GetInt64());
            Assert.Equal(0, dryRunDocument.RootElement.GetProperty("targetAfter").GetProperty("rowCounts").GetProperty("Items").GetInt64());

            var firstReport = await MigrationRunner.RunAsync(
                new MigrationOptions(
                    sourcePath,
                    targetConnectionString,
                    Apply: true,
                    AllowExistingTarget: false,
                    backupDirectory,
                    Path.Combine(root, "first-report.json")));

            Assert.True(firstReport.Succeeded, firstReport.Error);
            Assert.Equal("Applied", firstReport.Status);
            Assert.NotNull(firstReport.BackupPath);
            Assert.True(File.Exists(firstReport.BackupPath));
            Assert.Equal(sourceHashBefore, ComputeHash(sourcePath));
            Assert.Equal(2, firstReport.Source.RowCounts["Items"]);
            Assert.Equal(2, firstReport.Source.RowCounts["ItemBarcodes"]);
            Assert.Equal(2, firstReport.Source.RowCounts["Locations"]);
            Assert.Equal(1, firstReport.Source.RowCounts["Lots"]);
            Assert.Equal(1, firstReport.Source.RowCounts["Stock"]);
            Assert.Equal(1, firstReport.Source.RowCounts["Movements"]);

            await using (var targetContext = new WmsDbContext(targetOptions))
            {
                var item = await targetContext.Items
                    .Include(entity => entity.Barcodes)
                    .SingleAsync(entity => entity.Id == sourceIds.ItemId);
                Assert.Equal(sourceIds.ItemId, item.Id);
                Assert.Equal(2, await targetContext.Items.CountAsync());
                Assert.Equal(2, await targetContext.Locations.CountAsync());
                Assert.Equal(sourceIds.ParentLocationId, (await targetContext.Locations.SingleAsync(entity => entity.Id == sourceIds.ChildLocationId)).ParentLocationId);
                Assert.Equal(sourceIds.LotId, (await targetContext.Lots.SingleAsync()).Id);
                Assert.Equal(1, await targetContext.Stock.CountAsync());
                Assert.Equal(1, await targetContext.Movements.CountAsync());
                Assert.Single(item.Barcodes);
            }

            var secondReport = await MigrationRunner.RunAsync(
                new MigrationOptions(
                    sourcePath,
                    targetConnectionString,
                    Apply: true,
                    AllowExistingTarget: true,
                    backupDirectory,
                    Path.Combine(root, "second-report.json")));

            Assert.True(secondReport.Succeeded, secondReport.Error);
            Assert.Equal(2, secondReport.TargetAfter.RowCounts["Items"]);
            Assert.Equal(sourceHashBefore, ComputeHash(sourcePath));

            File.Copy(sourcePath, invalidSourcePath);
            await MakeSourceInvalidAsync(invalidSourcePath, sourceIds.SecondItemId);
            var invalidSourceHash = ComputeHash(invalidSourcePath);

            var failedReport = await MigrationRunner.RunAsync(
                new MigrationOptions(
                    invalidSourcePath,
                    targetConnectionString,
                    Apply: true,
                    AllowExistingTarget: true,
                    backupDirectory,
                    Path.Combine(root, "failed-report.json")));

            Assert.False(failedReport.Succeeded);
            Assert.Contains("duplicate", failedReport.Error!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(invalidSourceHash, ComputeHash(invalidSourcePath));

            await using (var targetContext = new WmsDbContext(targetOptions))
            {
                Assert.Equal(2, await targetContext.Items.CountAsync());
                Assert.Equal(1, await targetContext.Stock.CountAsync());
                Assert.Equal(1, await targetContext.Movements.CountAsync());
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<(int ItemId, int SecondItemId, int ParentLocationId, int ChildLocationId, int LotId)> CreateRepresentativeSourceAsync(
        string sourcePath)
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite($"Data Source={sourcePath};Pooling=False")
            .Options;
        await using var context = new WmsDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var warehouse = new Warehouse("MAIN", "Migration Warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var parentLocation = new Location("ZONE-1", "Zone 1", warehouse.Id);
        context.Locations.Add(parentLocation);
        await context.SaveChangesAsync();

        var childLocation = new Location("BIN-1", "Bin 1", warehouse.Id, parentLocation.Id);
        context.Locations.Add(childLocation);

        var item = new Item("MIGRATION-ITEM-001", "Migration Item", "EA", requiresLot: true);
        item.AddBarcode(new Barcode("MIGRATION-001"));
        var secondItem = new Item("MIGRATION-ITEM-002", "Second Migration Item", "EA");
        secondItem.AddBarcode(new Barcode("MIGRATION-002"));
        context.Items.AddRange(item, secondItem);
        await context.SaveChangesAsync();

        var lot = new Lot("MIGRATION-LOT-001", item.Id, DateTime.UtcNow.Date.AddDays(30), DateTime.UtcNow.Date.AddDays(-30));
        context.Lots.Add(lot);
        await context.SaveChangesAsync();

        var stock = new Stock(item.Id, childLocation.Id, new Quantity(12.5m), lot.Id);
        stock.ReserveQuantity(new Quantity(2.5m));
        context.Stock.Add(stock);

        var movement = Movement.CreateReceipt(
            item.Id,
            childLocation.Id,
            new Quantity(12.5m),
            "migration-test",
            lot.Id,
            referenceNumber: "MIGRATION-RECEIPT",
            notes: "Representative migration movement");
        context.Movements.Add(movement);
        await context.SaveChangesAsync();

        return (item.Id, secondItem.Id, parentLocation.Id, childLocation.Id, lot.Id);
    }

    private static async Task MakeSourceInvalidAsync(string sourcePath, int itemId)
    {
        await using var connection = new SqliteConnection($"Data Source={sourcePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP INDEX \"IX_Items_Sku\";";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "UPDATE \"Items\" SET \"Sku\" = 'MIGRATION-ITEM-001' WHERE \"Id\" = $id;";
        command.Parameters.AddWithValue("$id", itemId);
        await command.ExecuteNonQueryAsync();
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}

public sealed class DataMigrationFactAttribute : FactAttribute
{
    public DataMigrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Set WARECOMMAND_TEST_POSTGRES_CONNECTION or run scripts/verify-data-migration.ps1.";
        }
    }
}
