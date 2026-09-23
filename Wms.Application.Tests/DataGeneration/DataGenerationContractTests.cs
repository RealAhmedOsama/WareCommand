using FluentAssertions;
using Wms.Application.DataGeneration;

namespace Wms.Application.Tests.DataGeneration;

public sealed class DataGenerationContractTests
{
    [Fact]
    public void SameProfileSeedAndLocaleProduceTheSamePlan()
    {
        var request = new DataGenerationRequest(
            WmsDataGenerationProfiles.FullDemo,
            "release-102",
            Locale: "ar-EG");

        var first = DataGenerationPlan.Create(request);
        var second = DataGenerationPlan.Create(request);

        first.Should().BeEquivalentTo(second);
        first.SeedFingerprint.Should().HaveLength(64);
        first.PreviewRecords.Should().Contain(record => record.Values["warehouseName"].Contains("مخزن"));
    }

    [Fact]
    public void ProductionStagingUnknownTargetsAndResetsAreRejected()
    {
        var production = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "seed",
            Environment: "Production"));
        var unconfirmedReset = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "seed",
            ResetExisting: true));
        var unknownEnvironment = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "seed",
            Environment: "Preview-Unknown"));

        production.Should().Throw<InvalidOperationException>();
        unconfirmedReset.Should().Throw<InvalidOperationException>();
        unknownEnvironment.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProfilesExposeBoundedIntendedScales()
    {
        WmsDataGenerationProfiles.All.Should().HaveCount(5);
        var plan = DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.IntegrationTest,
            "seed",
            Scale: 2));

        plan.EstimatedCounts["warehouses"].Should().Be(4);
        plan.EstimatedCounts["inventoryRows"].Should().Be(400);
        plan.PreviewRecords.Should().NotBeEmpty();
        WmsDataGenerationProfiles.All.Should().OnlyContain(profile => profile.MaximumScale >= 1);
    }

    [Fact]
    public void LargePerformanceScaleCannotExceedItsExplicitResourceBudget()
    {
        var plan = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.LargePerformance,
            "perf-seed"));
        var unbounded = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.LargePerformance,
            "perf-seed",
            Scale: 2));

        plan.Should().NotThrow();
        unbounded.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OnlyEnglishAndArabicLocalesAreAccepted()
    {
        var unsupported = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.FullDemo,
            "seed",
            Locale: "fr-FR"));

        unsupported.Should().Throw<ArgumentException>();
    }
}
