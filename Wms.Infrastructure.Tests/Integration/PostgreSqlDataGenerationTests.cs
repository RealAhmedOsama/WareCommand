using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.DataGeneration;

namespace Wms.Infrastructure.Tests.Integration;

public sealed class PostgreSqlDataGenerationTests
{
    private static readonly JsonSerializerOptions EvidenceSerializerOptions = new() { WriteIndented = true };

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
        WriteDataGenerationEvidence(first, second);

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

    private static void WriteDataGenerationEvidence(params DataGenerationRunReport[] reports)
    {
        var path = Environment.GetEnvironmentVariable("WARECOMMAND_DATA_GENERATION_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var json = JsonSerializer.Serialize(new { reports }, EvidenceSerializerOptions);
        File.WriteAllText(path, json);
    }
}
