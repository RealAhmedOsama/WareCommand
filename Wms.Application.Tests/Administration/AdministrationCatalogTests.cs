using FluentAssertions;
using Wms.Application.Administration;
using Wms.Application.Identity;

namespace Wms.Application.Tests.Administration;

public sealed class AdministrationCatalogTests
{
    [Fact]
    public void Catalog_has_unique_routes_and_known_permissions()
    {
        var items = AdministrationCatalog.Items;

        items.Should().NotBeEmpty();
        items.Select(item => item.Key).Should().OnlyHaveUniqueItems();
        items.Select(item => item.Route).Should().OnlyHaveUniqueItems();
        items.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.TitleArabic) &&
            !string.IsNullOrWhiteSpace(item.Route) &&
            WmsPermissions.IsKnown(item.RequiredPermission));
        items.Should().Contain(item => item.SecretPolicy == AdministrationSecretPolicy.NeverDisplay);
        items.Should().Contain(item => item.SecretPolicy == AdministrationSecretPolicy.DeploymentManaged);
    }

    [Fact]
    public void Catalog_keeps_mutating_actions_inside_owned_typed_boundaries()
    {
        AdministrationCatalog.Items
            .SelectMany(item => item.SupportedActions)
            .Should()
            .NotContain("database-edit");

        AdministrationCatalog.Items
            .Where(item => item.SecretPolicy != AdministrationSecretPolicy.NotApplicable)
            .Should()
            .OnlyContain(item => item.SecretPolicy == AdministrationSecretPolicy.NeverDisplay ||
                                 item.SecretPolicy == AdministrationSecretPolicy.DeploymentManaged);
    }
}
