using System.Net;
using System.Text.RegularExpressions;
using Wms.Application.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class BrowserClientQualificationTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task Authenticated_arabic_scanner_shell_is_renderable_at_handheld_viewport_contract()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var picking = await client.GetAsync("/Picking?culture=ar-SA&ui-culture=ar-SA");
        var pickingBody = await picking.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, picking.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", pickingBody, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", pickingBody, StringComparison.Ordinal);
        Assert.Contains("type=\"number\"", pickingBody, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", pickingBody, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("FormatException|Internal Server Error", RegexOptions.IgnoreCase), pickingBody);

        using var manifest = await client.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        using var serviceWorker = await client.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, serviceWorker.StatusCode);

        using var styles = await client.GetAsync("/css/site.css");
        var stylesBody = await styles.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, styles.StatusCode);
        Assert.Contains("focus-visible", stylesBody, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", stylesBody, StringComparison.Ordinal);
    }
}
