using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identification;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ScanningFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task AuthenticatedScannerCanResolveAnItemAndRejectMalformedGs1()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        const string barcode = "API-SCAN-001";
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            var registry = scope.ServiceProvider.GetRequiredService<IIdentificationRegistry>();
            var item = new Item("API-SCAN-ITEM", "API scan item", "EA");
            item.AddBarcode(new Barcode(barcode));
            context.Items.Add(item);
            Assert.True((await registry.SyncItemAsync(item)).IsSuccess);
            await context.SaveChangesAsync();
        }

        using var page = await client.GetAsync("/Items/Create");
        var pageBody = await page.Content.ReadAsStringAsync();
        var tokenMatch = Regex.Match(
            pageBody,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(tokenMatch.Success);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/scanning/resolve")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { value = barcode }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("RequestVerificationToken", tokenMatch.Groups[1].Value);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Item", resolved.GetProperty("kind").GetString());
        Assert.Equal("API-SCAN-ITEM", resolved.GetProperty("itemSku").GetString());

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/scanning/resolve")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { value = "(01)00012345678904" }),
                Encoding.UTF8,
                "application/json")
        };
        malformed.Headers.Add("RequestVerificationToken", tokenMatch.Groups[1].Value);
        using var invalidResponse = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        var invalidBody = await invalidResponse.Content.ReadAsStringAsync();
        Assert.Contains("check digit", invalidBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("00012345678904", invalidBody, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
