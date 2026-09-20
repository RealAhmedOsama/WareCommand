using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Auditing;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class UnitOfMeasureFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task AdministratorCanManageLocalizedUnitCatalogAndItemAssignment()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var index = await client.GetAsync("/UnitsOfMeasure?culture=en-US&ui-culture=en-US");
        var indexBody = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Units of measure", indexBody, StringComparison.Ordinal);
        Assert.Contains("EA", indexBody, StringComparison.Ordinal);

        using var createPage = await client.GetAsync("/UnitsOfMeasure/Create?culture=en-US&ui-culture=en-US");
        var createToken = ExtractAntiForgeryToken(await createPage.Content.ReadAsStringAsync());
        var code = "BOX" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        using var create = await client.PostAsync(
            "/UnitsOfMeasure/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = code,
                ["Category"] = "Count",
                ["Precision"] = "0",
                ["Symbol"] = "box",
                ["Name"] = "Box",
                ["LocalizedName"] = "صندوق",
                ["__RequestVerificationToken"] = createToken
            }));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        var itemId = await CreateItemAsync();
        using var assignmentPage = await client.GetAsync($"/UnitsOfMeasure/Item?id={itemId}");
        var assignmentToken = ExtractAntiForgeryToken(await assignmentPage.Content.ReadAsStringAsync());
        using var assignment = await client.PostAsync(
            "/UnitsOfMeasure/Item",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ItemId"] = itemId.ToString(CultureInfo.InvariantCulture),
                ["BaseUnitOfMeasure"] = "EA",
                ["PurchaseUnitOfMeasure"] = code,
                ["SalesUnitOfMeasure"] = "EA",
                ["AllowFractionalQuantity"] = "true",
                ["ConversionsText"] = $"{code}|EA|12|0|Reject",
                ["__RequestVerificationToken"] = assignmentToken
            }));
        Assert.Equal(HttpStatusCode.Redirect, assignment.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var item = await context.Items.SingleAsync(candidate => candidate.Id == itemId);
        Assert.Equal(code, item.PurchaseUnit);
        Assert.True(await context.ItemUnitConversions.AnyAsync(conversion =>
            conversion.ItemId == itemId &&
            conversion.FromUnitOfMeasure == code &&
            conversion.ConversionFactor == 12m));
        Assert.True(await context.AuditEntries.AnyAsync(entry =>
            entry.Action == WmsAuditActions.ItemUnitsAssigned &&
            entry.EntityId == item.Sku));

        using var arabic = await client.GetAsync("/UnitsOfMeasure?culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = System.Net.WebUtility.HtmlDecode(await arabic.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("وحدات القياس", arabicBody, StringComparison.Ordinal);
    }

    private async Task<int> CreateItemAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var item = new Item(
            "UOM-ITEM-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            "Unit test item",
            "EA");
        context.Items.Add(item);
        await context.SaveChangesAsync();
        return item.Id;
    }

    private static HttpClient CreateClient(WareCommandWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private HttpClient CreateClient() => CreateClient(factory);

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Could not find an antiforgery token in the response.");
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
