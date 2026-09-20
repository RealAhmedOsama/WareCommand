using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ItemManagementFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task WarehouseStaffCanReadItemsButCannotManageThem()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var index = await client.GetAsync("/Items");
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);

        using var denied = await client.GetAsync("/Items/Create");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task AdministratorCanCreateAndReadFullLocalizedItemMaster()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var createPage = await client.GetAsync("/Items/Create?culture=en-US&ui-culture=en-US");
        var token = ExtractAntiForgeryToken(await createPage.Content.ReadAsStringAsync());
        var sku = "WEB-ITEM-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var barcode = "9" + Guid.NewGuid().ToString("N")[..7];
        var packagingBarcode = "8" + Guid.NewGuid().ToString("N")[..7];
        using var create = await client.PostAsync(
            "/Items/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Sku"] = sku,
                ["Name"] = "Web item",
                ["LocalizedName"] = "صنف الويب",
                ["Description"] = "Commercial item",
                ["LocalizedDescription"] = "صنف تجاري",
                ["Category"] = "Hardware",
                ["Brand"] = "Acme",
                ["Type"] = "FinishedGood",
                ["LifecycleStatus"] = "Active",
                ["BaseUnit"] = "EA",
                ["PurchaseUnit"] = "BOX",
                ["SalesUnit"] = "EA",
                ["RequiresExpiry"] = "true",
                ["ShelfLifeDays"] = "30",
                ["UseFefo"] = "true",
                ["StandardCost"] = "12.50",
                ["SalesPrice"] = "20.00",
                ["StorageProfile"] = "Standard",
                ["PutawayProfile"] = "Fast",
                ["BarcodesText"] = barcode,
                ["PackagingsText"] = $"CASE|EA|12|{packagingBarcode}|||||true|Case|علبة|0123456789012||Case|Allow|true|true|false|true|true",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.StartsWith("/Items/Details", create.Headers.Location?.OriginalString, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var item = await context.Items
            .Include(value => value.Packagings)
            .SingleAsync(value => value.Sku == sku);
        Assert.Equal("BOX", item.PurchaseUnit);
        Assert.True(item.RequiresExpiry);
        Assert.Single(item.Packagings);
        Assert.Equal("0123456789012", item.Packagings.Single().Gtin);
        Assert.Equal(PackagingType.Case, item.Packagings.Single().Type);
        Assert.True(item.Packagings.Single().IsDefaultShipping);
        Assert.True(await context.AuditEntries.AnyAsync(entry =>
            entry.Action == WmsAuditActions.ItemCreated && entry.EntityId == sku));
        Assert.True(await context.AuditEntries.AnyAsync(entry =>
            entry.Action == WmsAuditActions.ItemPackagingsChanged && entry.EntityId == sku));

        using var index = await client.GetAsync($"/Items?searchTerm={sku}&pageSize=1");
        var indexBody = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains(sku, indexBody, StringComparison.Ordinal);

        using var details = await client.GetAsync($"/Items/Details?id={item.Id}&culture=en-US&ui-culture=en-US");
        var detailsBody = await details.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Contains("Commercial details", detailsBody, StringComparison.Ordinal);
        Assert.Contains(packagingBarcode.ToUpperInvariant(), detailsBody, StringComparison.Ordinal);

        using var arabic = await client.GetAsync($"/Items/Details?id={item.Id}&culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = WebUtility.HtmlDecode(await arabic.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("البيانات التجارية", arabicBody, StringComparison.Ordinal);
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
