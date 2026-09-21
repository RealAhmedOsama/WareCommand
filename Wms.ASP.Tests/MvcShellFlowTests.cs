using System.Net;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class MvcShellFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task AuthenticatedInventoryShellRendersScannerCommandBarInBothDirections()
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

        using var english = await client.GetAsync("/Inventory?culture=en-US&ui-culture=en-US");
        var englishBody = await english.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, english.StatusCode);
        Assert.Contains("<html lang=\"en-US\" dir=\"ltr\">", englishBody, StringComparison.Ordinal);
        Assert.Contains("href=\"#main-content\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("data-wms-command-bar", englishBody, StringComparison.Ordinal);
        Assert.Contains("data-wms-quick-scan", englishBody, StringComparison.Ordinal);
        Assert.Contains("id=\"wms-quick-scan-input\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("name=\"searchTerm\"", englishBody, StringComparison.Ordinal);
        Assert.Contains("Scan a barcode, SKU, or location", englishBody, StringComparison.Ordinal);
        Assert.Contains("data-wms-direction=\"ltr\"", englishBody, StringComparison.Ordinal);

        using var arabic = await client.GetAsync("/Inventory?culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = await arabic.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("امسح باركود أو أدخل الصنف أو الموقع", WebUtility.HtmlDecode(arabicBody), StringComparison.Ordinal);
        Assert.Contains("data-wms-direction=\"rtl\"", arabicBody, StringComparison.Ordinal);
    }
}
