using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class OperationalDashboardFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task DashboardRendersScopedInventoryKpisWithoutAQuantityAsCurrency()
    {
        var user = await factory.CreateUserAsync();
        await factory.CreateWarehouseScenarioAsync(user.Id);
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

        using var english = await client.GetAsync(
            "/Dashboard?culture=en-US&ui-culture=en-US");
        var englishBody = await english.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        Assert.Contains("<html lang=\"en-US\" dir=\"ltr\">", englishBody, StringComparison.Ordinal);
        Assert.Contains("Inventory KPIs", englishBody, StringComparison.Ordinal);
        Assert.Contains("On hand", englishBody, StringComparison.Ordinal);
        Assert.Contains("Available", englishBody, StringComparison.Ordinal);
        Assert.Contains("Expiring soon", englishBody, StringComparison.Ordinal);
        Assert.Contains("Total Units", englishBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Total Stock Value", englishBody, StringComparison.Ordinal);

        using var arabic = await client.GetAsync(
            "/Dashboard?culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = await arabic.Content.ReadAsStringAsync();
        var decodedArabicBody = WebUtility.HtmlDecode(arabicBody);
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("مؤشرات المخزون", decodedArabicBody, StringComparison.Ordinal);
        Assert.Contains("المتاح فعلياً", decodedArabicBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshReturnsFreshReceiptSnapshotAndRejectsAnUnassignedWarehouse()
    {
        var user = await factory.CreateUserAsync();
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.WarehouseManager);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var before = await client.GetAsync("/Dashboard/RefreshData");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var beforeJson = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
        Assert.Equal(10m, ReadDecimal(beforeJson.RootElement, "inventory", "data", "onHandQuantity"));

        using var receivingPage = await client.GetAsync("/Receiving/Receive");
        var receivingHtml = await receivingPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, receivingPage.StatusCode);
        var tokenMatch = Regex.Match(
            receivingHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(tokenMatch.Success, "Could not find the receiving antiforgery token.");

        using var receipt = await client.PostAsync(
            "/Receiving/Receive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ItemSku"] = scenario.ItemSku,
                ["LocationCode"] = scenario.AssignedLocationCode,
                ["Quantity"] = "5",
                ["UnitOfMeasure"] = "EA",
                ["ReferenceNumber"] = "DASHBOARD-REF",
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
            }));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);

        using var after = await client.GetAsync("/Dashboard/RefreshData");
        var afterBody = await after.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var afterJson = JsonDocument.Parse(afterBody);
        Assert.Equal(15m, ReadDecimal(afterJson.RootElement, "inventory", "data", "onHandQuantity"));
        Assert.Equal(15m, ReadDecimal(afterJson.RootElement, "inventory", "data", "availableQuantity"));
        Assert.Equal("Available", ReadString(afterJson.RootElement, "recentMovements", "status"));
        var recentMovements = ReadPath(afterJson.RootElement, ["recentMovements", "data"]);
        Assert.NotEmpty(recentMovements.EnumerateArray());
        Assert.Equal("Receipt", recentMovements[0].GetProperty("type").GetString());
        Assert.Equal(5m, recentMovements[0].GetProperty("quantity").GetDecimal());

        using var refreshedPage = await client.GetAsync("/Dashboard?culture=en-US&ui-culture=en-US");
        var refreshedHtml = await refreshedPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, refreshedPage.StatusCode);
        Assert.Contains("data-dashboard-value=\"inventory.onHandQuantity\">15.0</div>", refreshedHtml, StringComparison.Ordinal);
        Assert.Contains("dashboardRefreshStatus", refreshedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("window.location.reload()", refreshedHtml, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var unassignedWarehouseId = await context.Locations
            .Where(location => location.Code == scenario.UnassignedLocationCode)
            .Select(location => location.WarehouseId)
            .SingleAsync();
        using var crossWarehouse = await client.GetAsync(
            $"/Dashboard/RefreshData?warehouseId={unassignedWarehouseId}");
        var crossWarehouseBody = await crossWarehouse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, crossWarehouse.StatusCode);
        Assert.DoesNotContain("DASHBOARD-REF", crossWarehouseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshMarksReportFieldsForbiddenWhenTheUserLacksReportsPermission()
    {
        var user = await factory.CreateUserAsync();
        await factory.CreateWarehouseScenarioAsync(user.Id);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/Dashboard/RefreshData");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var snapshot = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var movements = snapshot.RootElement.GetProperty("recentMovements");
        Assert.Equal("Forbidden", movements.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, movements.GetProperty("data").ValueKind);
    }

    private HttpClient CreateClient() => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static decimal ReadDecimal(JsonElement element, params string[] path) =>
        ReadPath(element, path).GetDecimal();

    private static string? ReadString(JsonElement element, params string[] path) =>
        ReadPath(element, path).GetString();

    private static JsonElement ReadPath(JsonElement element, IReadOnlyList<string> path)
    {
        foreach (var segment in path)
        {
            element = element.GetProperty(segment);
        }

        return element;
    }
}
