using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Wms.Application.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class AdministrationFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task AdministrationConsole_requires_access_manage_and_supports_localized_admins()
    {
        var deniedUser = await factory.CreateUserAsync();
        using var deniedClient = CreateClient();
        using var deniedLogin = await AuthenticationFlowTests.PostLoginAsync(
            deniedClient,
            deniedUser.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, deniedLogin.StatusCode);

        using var denied = await deniedClient.GetAsync("/Administration");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);

        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        using var allowedClient = CreateClient();
        using var allowedLogin = await AuthenticationFlowTests.PostLoginAsync(
            allowedClient,
            administrator.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, allowedLogin.StatusCode);

        using var english = await allowedClient.GetAsync("/Administration?culture=en-US&ui-culture=en-US");
        var englishBody = await english.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        Assert.Contains("<html lang=\"en-US\" dir=\"ltr\">", englishBody, StringComparison.Ordinal);
        Assert.Contains("Administration catalog", englishBody, StringComparison.Ordinal);
        Assert.Contains("Security boundary", englishBody, StringComparison.Ordinal);
        Assert.Contains("Inbound execution", englishBody, StringComparison.Ordinal);

        using var arabic = await allowedClient.GetAsync("/Administration?culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = await arabic.Content.ReadAsStringAsync();
        var decodedArabicBody = WebUtility.HtmlDecode(arabicBody);
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("كتالوج الإدارة", decodedArabicBody, StringComparison.Ordinal);
        Assert.Contains("حد الأمان", decodedArabicBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administration_api_returns_authorized_catalog_and_paged_history()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            administrator.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var catalog = await client.GetAsync("/api/administration/catalog");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        var catalogBody = await catalog.Content.ReadAsStringAsync();
        Assert.Contains("organization.settings", catalogBody, StringComparison.Ordinal);
        Assert.Contains("secretPolicy", catalogBody, StringComparison.Ordinal);

        using var history = await client.GetAsync("/api/administration/history?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyBody = await history.Content.ReadAsStringAsync();
        Assert.Contains("totalCount", historyBody, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
