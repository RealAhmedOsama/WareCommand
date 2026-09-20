using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
