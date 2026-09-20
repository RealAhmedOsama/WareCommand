using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Tests.Entities;

public sealed class WarehouseTests
{
    [Fact]
    public void UpdateProfile_DoesNotChangeImmutableCode()
    {
        var warehouse = new Warehouse("main", "Main", "الرئيسي");

        warehouse.UpdateProfile(
            "Updated Main",
            "المستودع الرئيسي",
            "Address",
            "Contact",
            "0123",
            "contact@example.test",
            "Africa/Cairo",
            allowNegativeStock: false,
            requireLocationForAdjustment: true,
            blockExpiredReceipt: true,
            expiryWarningDays: 14);

        warehouse.Code.Should().Be("MAIN");
        warehouse.Name.Should().Be("Updated Main");
        warehouse.ArabicName.Should().Be("المستودع الرئيسي");
        warehouse.TimeZone.Should().Be("Africa/Cairo");
    }

    [Fact]
    public void EnableWorkflows_RequiresEveryOperationalRole()
    {
        var warehouse = new Warehouse("MAIN", "Main");

        var act = () => warehouse.EnableWorkflows([
            WarehouseOperationalLocationRole.Receiving,
            WarehouseOperationalLocationRole.Storage]);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Staging");
        warehouse.WorkflowEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnableAndDeactivateWorkflows_PreservesWarehouseIdentity()
    {
        var warehouse = new Warehouse("MAIN", "Main");
        warehouse.EnableWorkflows(Warehouse.RequiredOperationalLocationRoles);

        warehouse.WorkflowEnabled.Should().BeTrue();
        warehouse.Deactivate();

        warehouse.IsActive.Should().BeFalse();
        warehouse.WorkflowEnabled.Should().BeFalse();
        warehouse.Code.Should().Be("MAIN");
    }
}
