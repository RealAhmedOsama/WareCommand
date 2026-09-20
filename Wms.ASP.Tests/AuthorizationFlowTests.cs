using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Wms.Application.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class AuthorizationFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task AuthenticatedUserWithoutPermissionCannotOpenProtectedOperation()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(client, user.UserName!, "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Items");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task PermissionRemovalTakesEffectForAnExistingAuthenticatedSession()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(client, user.UserName!, "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var allowed = await client.GetAsync("/Items");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        await factory.RemoveAllRolesAsync(user.Id);

        using var denied = await client.GetAsync("/Items");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task InventoryViewContainsOnlyAssignedWarehouseData()
    {
        var user = await factory.CreateUserAsync();
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(client, user.UserName!, "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Inventory");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(scenario.AssignedLocationCode, body, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.UnassignedLocationCode, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuditScreenRequiresPermissionAndRendersForAuditor()
    {
        var unprivilegedUser = await factory.CreateUserAsync();
        using var deniedClient = CreateClient();
        using var deniedLogin = await AuthenticationFlowTests.PostLoginAsync(
            deniedClient,
            unprivilegedUser.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, deniedLogin.StatusCode);

        using var denied = await deniedClient.GetAsync("/Audit");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        var auditor = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(auditor.Id, WmsRoleNames.Auditor);
        using var allowedClient = CreateClient();
        using var allowedLogin = await AuthenticationFlowTests.PostLoginAsync(
            allowedClient,
            auditor.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, allowedLogin.StatusCode);

        using var allowed = await allowedClient.GetAsync("/Audit");
        var body = await allowed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("Audit log", body, StringComparison.Ordinal);

        using var export = await allowedClient.GetAsync("/Audit/Export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
