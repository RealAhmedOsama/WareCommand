using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class InventoryStatusTests
{
    [Fact]
    public void AllocatableStatusMustAlsoBeAvailable()
    {
        var act = () => new InventoryStatus(
            "ALLOC",
            "Allocatable",
            "قابل للتخصيص",
            isAvailable: false,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*allocatable*");
    }

    [Fact]
    public void SystemStatusCannotBeDisabled()
    {
        var status = new InventoryStatus(
            InventoryStatusCodes.Available,
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true,
            isSystem: true);

        var act = () => status.Deactivate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TransitionRejectsSelfTransition()
    {
        var act = () => new InventoryStatusTransition(
            InventoryStatusSystemIds.Available,
            InventoryStatusSystemIds.Available);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CatalogContainsRestrictedSystemStatuses()
    {
        InventoryStatusCatalog.SystemStatuses.Should().Contain(status =>
            status.Code == InventoryStatusCodes.Quarantine &&
            !status.IsAllocatable &&
            status.ForceForLocationType);
        InventoryStatusCatalog.SystemStatuses.Should().Contain(status =>
            status.Code == InventoryStatusCodes.Damaged &&
            !status.IsPickable);
    }
}
