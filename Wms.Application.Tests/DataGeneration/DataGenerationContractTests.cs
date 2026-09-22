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
    public void ProductionAndStagingResetsAreRejected()
    {
        var production = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "seed",
            Environment: "Production"));
        var unconfirmedReset = () => DataGenerationPlan.Create(new DataGenerationRequest(
            WmsDataGenerationProfiles.MinimalDevelopment,
            "seed",
            ResetExisting: true));

        production.Should().Throw<InvalidOperationException>();
        unconfirmedReset.Should().Throw<InvalidOperationException>();
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
    }
}
