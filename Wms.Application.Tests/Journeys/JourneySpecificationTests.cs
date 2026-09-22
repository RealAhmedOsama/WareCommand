using FluentAssertions;
using Wms.Application.Journeys;

namespace Wms.Application.Tests.Journeys;

public sealed class JourneySpecificationTests
{
    [Fact]
    public void Catalog_contains_independent_scoped_journeys_with_full_reconciliation()
    {
        JourneyCatalog.CriticalJourneys.Should().HaveCount(3);
        JourneyCatalog.CriticalJourneys.Select(journey => journey.Kind)
            .Should().Contain([WarehouseJourneyKind.Inbound, WarehouseJourneyKind.Outbound, WarehouseJourneyKind.Transfer]);

        foreach (var journey in JourneyCatalog.CriticalJourneys)
        {
            JourneySpecificationPolicy.Validate(journey).IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public void Retry_and_concurrency_flags_require_explicit_retry_steps()
    {
        var invalid = JourneyCatalog.CriticalJourneys[0] with
        {
            IncludesRetry = true
        };

        var result = JourneySpecificationPolicy.Validate(invalid);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("journey.retry_missing");
    }

    [Fact]
    public void Passing_run_requires_zero_reconciliation_errors()
    {
        var specification = JourneyCatalog.CriticalJourneys[1];
        var result = JourneySpecificationPolicy.ValidateRunResult(
            specification,
            new JourneyRunResult(
                specification.JourneyId,
                JourneyOutcome.Pass,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow,
                specification.Steps.Count,
                1,
                ["order-1", "shipment-1"],
                []));

        result.ErrorCode.Should().Be("journey.reconciliation_failed");
    }

    [Fact]
    public void Arabic_journey_is_retained_as_an_explicit_qualification_case()
    {
        var arabic = JourneyCatalog.CriticalJourneys.Single(journey =>
            journey.Locale.StartsWith("ar", StringComparison.OrdinalIgnoreCase));

        arabic.IsHappyPath.Should().BeFalse();
        arabic.IncludesPartialOrException.Should().BeTrue();
        JourneySpecificationPolicy.Validate(arabic).IsSuccess.Should().BeTrue();
    }
}
