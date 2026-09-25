using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.DataGeneration;

namespace Wms.Infrastructure.Tests.Integration;

public sealed class PostgreSqlDataGenerationTests
{
    [PostgreSqlFact]
    public async Task SameSeedProducesEquivalentServiceBackedDatasetsInSeparateSchemas()
    {
        await using var firstTarget = new PostgreSqlTestDatabase();
        await using var secondTarget = new PostgreSqlTestDatabase();
        await firstTarget.InitializeAsync();
        await secondTarget.InitializeAsync();

        var request = new DataGenerationRequest(
            WmsDataGenerationProfiles.IntegrationTest,
            "issue-129-repeatability",
            Environment: "Testing",
            Locale: "ar-EG");
        var first = await DeterministicPostgreSqlDataGenerationFixture.WriteAsync(firstTarget, request);
        var second = await DeterministicPostgreSqlDataGenerationFixture.WriteAsync(secondTarget, request);

        Assert.Equal(first.SeedFingerprint, second.SeedFingerprint);
        Assert.Equal(first.GeneratorVersion, second.GeneratorVersion);
        Assert.Equal(first.Profile, second.Profile);
        Assert.NotEqual(first.TargetIdentifier, second.TargetIdentifier);
        Assert.Equal(first.ActualCounts, second.ActualCounts);
        Assert.Equal(first.OperationOutcomes, second.OperationOutcomes);
        Assert.Equal(first.LogicalDatasetFingerprint, second.LogicalDatasetFingerprint);
        Assert.True(first.ReconciliationClean, $"First schema reported {first.ReconciliationIssueCount} reconciliation issues.");
        Assert.True(second.ReconciliationClean, $"Second schema reported {second.ReconciliationIssueCount} reconciliation issues.");
        Assert.Equal(0, first.ReconciliationIssueCount);
        Assert.Equal(0, second.ReconciliationIssueCount);
        Assert.Equal(200, first.ActualCounts["inventoryRows"]);
        Assert.Equal(2, first.ActualCounts["warehouses"]);
        Assert.Equal(40, first.ActualCounts["locations"]);
        Assert.Equal(100, first.ActualCounts["items"]);
        Assert.True(first.ActualCounts["purchaseOrders"] > 0);
        Assert.True(first.ActualCounts["receipts"] > 0);
        Assert.True(first.ActualCounts["salesOrders"] > 0);
        Assert.True(first.ActualCounts["reservations"] > 0);
        Assert.True(first.ActualCounts["reservationAllocations"] > 0);
        Assert.True(first.ActualCounts["shipments"] > 0);
        Assert.True(first.ActualCounts["returns"] > 0);
        Assert.True(first.ActualCounts["cycleCountTasks"] > 0);
        Assert.True(first.ActualCounts["cycleCountLines"] > 0);
        Assert.True(first.ActualCounts["lots"] > 0);
        Assert.True(first.ActualCounts["serials"] > 0);
        Assert.True(first.ActualCounts["licensePlates"] > 0);
        Assert.True(first.ActualCounts["owners"] > 0);
        Assert.Contains(first.OperationOutcomes, value => value.Contains("serial-and-external-owner", StringComparison.Ordinal));

        using var context = firstTarget.CreateContext();
        var balances = await context.InventoryBalances.AsNoTracking()
            .Include(value => value.Item)
            .Include(value => value.Lot)
            .Include(value => value.Serial)
            .Include(value => value.LicensePlate)
            .Include(value => value.InventoryOwner)
            .ToArrayAsync();
        Assert.Contains(balances, value =>
            value.Item.RequiresLot && value.Lot is not null && value.Lot.ItemId == value.ItemId);
        Assert.Contains(balances, value =>
            value.Item.RequiresSerial && value.Serial is not null &&
            value.Serial.ItemId == value.ItemId && value.Serial.Number == value.SerialNumber);
        Assert.Contains(balances, value =>
            value.LicensePlateId.HasValue && value.LicensePlate is not null);
        Assert.Contains(balances, value =>
            value.InventoryOwnerId.HasValue && value.InventoryOwner is not null &&
            value.OwnerCodeSnapshot == value.InventoryOwner.OwnerCode);

        var output = JsonSerializer.Serialize(first);
        Assert.DoesNotContain("password", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@example.test", output, StringComparison.OrdinalIgnoreCase);

        var rerun = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DeterministicPostgreSqlDataGenerationFixture.WriteAsync(firstTarget, request));
        Assert.Contains("never reset or reused", rerun.Message, StringComparison.OrdinalIgnoreCase);
        using var afterRejectedRerun = firstTarget.CreateContext();
        Assert.Equal(first.ActualCounts["warehouses"], await afterRejectedRerun.Warehouses.CountAsync());
        Assert.Equal(first.ActualCounts["inventoryRows"], await afterRejectedRerun.Stock.CountAsync());
    }

    [PostgreSqlFact]
    public async Task DemoAndEdgeCaseProfilesPopulateEnglishAndArabicJourneys()
    {
        var cases = new[]
        {
            (Profile: WmsDataGenerationProfiles.FullDemo, Seed: "issue-129-demo-en", Locale: "en-US", Warehouses: 2, Locations: 80, Items: 250, InventoryRows: 400),
            (Profile: WmsDataGenerationProfiles.FullDemo, Seed: "issue-129-demo-ar", Locale: "ar-EG", Warehouses: 2, Locations: 80, Items: 250, InventoryRows: 400),
            (Profile: WmsDataGenerationProfiles.EdgeCases, Seed: "issue-129-edge-ar", Locale: "ar-EG", Warehouses: 1, Locations: 12, Items: 40, InventoryRows: 80)
        };

        foreach (var profileCase in cases)
        {
            await using var target = new PostgreSqlTestDatabase();
            await target.InitializeAsync();
            var report = await DeterministicPostgreSqlDataGenerationFixture.WriteAsync(
                target,
                new DataGenerationRequest(
                    profileCase.Profile,
                    profileCase.Seed,
                    Environment: "Testing",
                    Locale: profileCase.Locale));
            Assert.Equal(profileCase.Profile, report.Profile);
            Assert.Equal(profileCase.Warehouses, report.ActualCounts["warehouses"]);
            Assert.Equal(profileCase.Locations, report.ActualCounts["locations"]);
            Assert.Equal(profileCase.Items, report.ActualCounts["items"]);
            Assert.Equal(profileCase.InventoryRows, report.ActualCounts["inventoryRows"]);
            Assert.True(report.ReconciliationClean, $"Profile {profileCase.Profile} reported {report.ReconciliationIssueCount} reconciliation issues.");
            Assert.Equal(0, report.ReconciliationIssueCount);
            Assert.Contains(report.OperationOutcomes, value => value.StartsWith("purchase-order=", StringComparison.Ordinal));
            Assert.Contains(report.OperationOutcomes, value => value.StartsWith("sales-order=", StringComparison.Ordinal));

            using var context = target.CreateContext();
            var warehouse = await context.Warehouses.AsNoTracking().OrderBy(value => value.Code).FirstAsync();
            if (profileCase.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
            {
                Assert.StartsWith("مخزن", warehouse.Name, StringComparison.Ordinal);
            }
            else
            {
                Assert.StartsWith("Warehouse", warehouse.Name, StringComparison.Ordinal);
            }

            if (profileCase.Profile == WmsDataGenerationProfiles.EdgeCases ||
                profileCase.Profile == WmsDataGenerationProfiles.FullDemo)
            {
                Assert.Contains(
                    report.OperationOutcomes,
                    value => value.Contains("serial-and-external-owner", StringComparison.Ordinal));
                Assert.True(report.ActualCounts["serials"] > 0);
                Assert.True(report.ActualCounts["owners"] > 0);
            }
        }
    }

    [PostgreSqlFact]
    public async Task ExplicitResetAndProductionTargetsAreRefusedBeforeWriting()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var request = new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "issue-129-refusal",
            Environment: "Testing");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeterministicPostgreSqlDataGenerationFixture.WriteAsync(
                target,
                request with { Environment = "Production" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeterministicPostgreSqlDataGenerationFixture.WriteAsync(
                target,
                request with { ResetExisting = true, ResetConfirmation = "RESET-NON-PRODUCTION" }));

        using var context = target.CreateContext();
        Assert.Empty(await context.Warehouses.ToArrayAsync());
    }

    [PostgreSqlFact]
    public async Task CancellationAndUnknownProfilesDoNotWriteAnything()
    {
        await using var target = new PostgreSqlTestDatabase();
        await using var uninitializedTarget = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var request = new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "issue-129-cancellation",
            Environment: "Testing");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DeterministicPostgreSqlDataGenerationFixture.WriteAsync(target, request, cancelled.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeterministicPostgreSqlDataGenerationFixture.WriteAsync(uninitializedTarget, request));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            DeterministicPostgreSqlDataGenerationFixture.WriteAsync(
                target,
                request with { Profile = "unsupported-profile" }));

        using var context = target.CreateContext();
        Assert.Empty(await context.Warehouses.ToArrayAsync());
        Assert.Empty(await context.Users.ToArrayAsync());
    }

}
