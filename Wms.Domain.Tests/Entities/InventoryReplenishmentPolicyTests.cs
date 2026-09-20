using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class InventoryReplenishmentPolicyTests
{
    private static readonly DateTime EffectiveFrom =
        new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ConstructorValidatesQuantityOrderingAndNormalizesEffectiveDates()
    {
        var policy = new InventoryReplenishmentPolicy(
            itemId: 10,
            warehouseId: 20,
            locationId: null,
            minimumQuantity: 2m,
            maximumQuantity: 20m,
            safetyStockQuantity: 4m,
            reorderPointQuantity: 6m,
            targetQuantity: 12m,
            quantityBasis: InventoryPolicyQuantityBasis.AvailableToPromise,
            effectiveFromUtc: DateTime.SpecifyKind(
                EffectiveFrom.AddHours(-2),
                DateTimeKind.Unspecified),
            effectiveToUtc: EffectiveFrom.AddDays(30),
            preferredSource: "  supplier-a  ",
            leadTimeDays: 5);

        policy.EffectiveFromUtc.Kind.Should().Be(DateTimeKind.Utc);
        policy.EffectiveToUtc.Should().Be(EffectiveFrom.AddDays(30));
        policy.PreferredSource.Should().Be("supplier-a");
        policy.IsEffectiveAt(EffectiveFrom).Should().BeTrue();

        var invalidOrdering = () => new InventoryReplenishmentPolicy(
            10,
            20,
            null,
            2m,
            20m,
            8m,
            7m,
            12m,
            InventoryPolicyQuantityBasis.OnHand,
            EffectiveFrom);

        invalidOrdering.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateIncrementsRevisionAndRejectsInvalidEffectiveRange()
    {
        var policy = new InventoryReplenishmentPolicy(
            10,
            20,
            30,
            1m,
            10m,
            2m,
            4m,
            8m,
            InventoryPolicyQuantityBasis.OnHand,
            EffectiveFrom);

        policy.Update(
            2m,
            12m,
            3m,
            5m,
            10m,
            InventoryPolicyQuantityBasis.PhysicalAvailable,
            EffectiveFrom.AddDays(1));

        policy.Revision.Should().Be(1);
        policy.MinimumQuantity.Should().Be(2m);
        policy.QuantityBasis.Should().Be(InventoryPolicyQuantityBasis.PhysicalAvailable);

        var invalidRange = () => policy.Update(
            1m,
            10m,
            2m,
            4m,
            8m,
            InventoryPolicyQuantityBasis.OnHand,
            EffectiveFrom.AddDays(3),
            EffectiveFrom.AddDays(2));

        invalidRange.Should().Throw<ArgumentException>();
        policy.Revision.Should().Be(1);
    }
}
