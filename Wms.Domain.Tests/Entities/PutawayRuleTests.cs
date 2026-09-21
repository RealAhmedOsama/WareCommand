using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class PutawayRuleTests
{
    [Fact]
    public void FixedRuleRequiresTargetLocation()
    {
        var action = () => new PutawayRule(
            1,
            "fixed",
            "Fixed",
            PutawayRuleStrategy.FixedLocation,
            100,
            DateTime.UtcNow);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EffectiveDateRangeMustBeOrdered()
    {
        var action = () => new PutawayRule(
            1,
            "range",
            "Range",
            PutawayRuleStrategy.CapacityAware,
            100,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(-1));

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RuleNormalizesCodesAndScopedValues()
    {
        var rule = new PutawayRule(
            1,
            " fixed ",
            "Fixed",
            PutawayRuleStrategy.FixedLocation,
            100,
            DateTime.UtcNow,
            fixedLocationId: 7,
            itemCategory: " widgets ",
            sourceProcess: " receipt ");

        rule.Code.Should().Be("FIXED");
        rule.ItemCategory.Should().Be("WIDGETS");
        rule.SourceProcess.Should().Be("RECEIPT");
        rule.IsActive.Should().BeTrue();
    }
}
