using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Wms.Application.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class AccessibilityFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task Anonymous_shell_exposes_keyboard_and_localization_landmarks()
    {
        using var client = CreateClient();
        using var english = await client.GetAsync("/Account/Login?culture=en-US&ui-culture=en-US");
        var englishBody = await english.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        Assert.Contains("<html lang=\"en-US\" dir=\"ltr\">", englishBody, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("class=\"wms-skip-link\" href=\"#main-content\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("role=\"main\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("for=\"UserName\"", englishBody, StringComparison.Ordinal);

        using var arabic = await client.GetAsync("/Account/Login?culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = WebUtility.HtmlDecode(await arabic.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("class=\"wms-skip-link\" href=\"#main-content\"", arabicBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorized_representative_pages_keep_landmarks_labels_and_status_text()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            administrator.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var dashboard = await client.GetAsync("/Dashboard?culture=en-US&ui-culture=en-US");
        var dashboardBody = await dashboard.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("aria-labelledby=\"inventory-kpis-heading\"", dashboardBody, StringComparison.Ordinal);
        Assert.Contains("wms-stat-grid", dashboardBody, StringComparison.Ordinal);

        using var reports = await client.GetAsync("/Reports?culture=ar-SA&ui-culture=ar-SA");
        var reportsBody = WebUtility.HtmlDecode(await reports.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, reports.StatusCode);
        Assert.Contains("dir=\"rtl\"", reportsBody, StringComparison.Ordinal);
        Assert.Contains("class=\"form-label\"", reportsBody, StringComparison.Ordinal);
        Assert.Contains("for=\"fromDate\"", reportsBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_styles_expose_focus_touch_and_reduced_motion_contracts()
    {
        using var client = CreateClient();
        using var css = await client.GetAsync("/css/site.css");
        var cssBody = await css.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains(".wms-touch-target", cssBody, StringComparison.Ordinal);
        Assert.Contains("body.wms-shell a:focus-visible", cssBody, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", cssBody, StringComparison.Ordinal);
        Assert.Contains(".wms-skeleton", cssBody, StringComparison.Ordinal);
        Assert.Contains(".wms-main-content:focus", cssBody, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
