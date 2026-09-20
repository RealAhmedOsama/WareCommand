using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class WarehouseManagementFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task WarehouseManagementRequiresWarehousePermission()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Warehouses");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessDenied", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task AdministratorCanCreateLocalizedWarehouseAndSeeScopedManagementSurface()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var createPage = await client.GetAsync("/Warehouses/Create");
        var createBody = await createPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var token = ExtractAntiForgeryToken(createBody);
        var code = "WEB-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        using var create = await client.PostAsync(
            "/Warehouses/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = code,
                ["Name"] = "Web Warehouse",
                ["ArabicName"] = "مستودع الويب",
                ["Address"] = "Warehouse Street",
                ["ContactName"] = "Operations",
                ["ContactPhone"] = "+201000000000",
                ["ContactEmail"] = "warehouse@example.test",
                ["TimeZone"] = "Africa/Cairo",
                ["AllowNegativeStock"] = "false",
                ["RequireLocationForAdjustment"] = "true",
                ["BlockExpiredReceipt"] = "true",
                ["ExpiryWarningDays"] = "30",
                ["NextReceiptNumber"] = "1",
                ["NextOrderNumber"] = "1",
                ["NextWorkNumber"] = "1",
                ["NextShipmentNumber"] = "1",
                ["NextTransferNumber"] = "1",
                ["NextCountNumber"] = "1",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.StartsWith("/Warehouses/Details", create.Headers.Location?.OriginalString, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var warehouse = await context.Warehouses.SingleAsync(item => item.Code == code);
        Assert.Equal("مستودع الويب", warehouse.ArabicName);
        Assert.Equal("Africa/Cairo", warehouse.TimeZone);
        Assert.False(warehouse.WorkflowEnabled);
        Assert.True(await context.WarehouseNumberSequences.AnyAsync(sequence => sequence.WarehouseId == warehouse.Id));
        Assert.True(await context.AuditEntries.AnyAsync(entry =>
            entry.Action == WmsAuditActions.WarehouseCreated &&
            entry.WarehouseId == warehouse.Id));

        using var index = await client.GetAsync("/Warehouses");
        var indexBody = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains(code, indexBody, StringComparison.Ordinal);
        Assert.Contains("Warehouse management", indexBody, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Could not find an antiforgery token in the response.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
