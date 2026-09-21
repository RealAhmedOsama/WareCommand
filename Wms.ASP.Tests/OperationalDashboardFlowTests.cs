using System.Net;
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
}
