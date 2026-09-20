using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class SettingsFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task SettingsRequiresSettingsPermission()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Settings");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task AdministratorCanViewExportAndImportTypedNonSecretSettings()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var page = await client.GetAsync("/Settings");
        var pageBody = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Global settings", pageBody, StringComparison.Ordinal);
        Assert.Contains("Business rules live here", pageBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", pageBody, StringComparison.OrdinalIgnoreCase);

        using var export = await client.GetAsync("/Settings/Export");
        var exportJson = await export.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/json", export.Content.Headers.ContentType?.MediaType);
        Assert.Contains("warehouseOverrides", exportJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exportJson, StringComparison.OrdinalIgnoreCase);

        var token = ExtractAntiForgeryToken(pageBody);
        using var import = await client.PostAsync(
            "/Settings/Import",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ImportJson"] = exportJson,
                ["__RequestVerificationToken"] = token
            }));
        Assert.Equal(HttpStatusCode.Redirect, import.StatusCode);
        Assert.Equal("/Settings", import.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var settingsAuditCount = await context.AuditEntries.CountAsync(entry =>
            entry.Action == WmsAuditActions.SettingsChanged);
        Assert.True(settingsAuditCount > 0);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Could not find an antiforgery token in the response.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
