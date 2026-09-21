using System.Net;
using System.Text.Json;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PwaFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task PublicPwaAssetsExposeStandaloneShellWithoutCachingAuthenticatedHtml()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        using var manifest = await client.GetAsync("/manifest.webmanifest");
        var manifestBody = await manifest.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Contains("json", manifest.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        using (var manifestDocument = JsonDocument.Parse(manifestBody))
        {
            var root = manifestDocument.RootElement;
            Assert.Equal("standalone", root.GetProperty("display").GetString());
            Assert.Equal("/", root.GetProperty("scope").GetString());
            Assert.Equal("portrait-primary", root.GetProperty("orientation").GetString());
            Assert.Equal(2, root.GetProperty("icons").GetArrayLength());
        }

        using var serviceWorker = await client.GetAsync("/sw.js");
        var serviceWorkerBody = await serviceWorker.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, serviceWorker.StatusCode);
        Assert.Contains("WMS_CACHE_VERSION", serviceWorkerBody, StringComparison.Ordinal);
        Assert.Contains("request.mode === 'navigate'", serviceWorkerBody, StringComparison.Ordinal);
        Assert.Contains("/offline.html", serviceWorkerBody, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage", serviceWorkerBody, StringComparison.OrdinalIgnoreCase);

        using var offline = await client.GetAsync("/offline.html");
        var offlineBody = await offline.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, offline.StatusCode);
        Assert.Contains("no inventory command was queued", offlineBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__RequestVerificationToken", offlineBody, StringComparison.Ordinal);

        using var login = await client.GetAsync("/Account/Login");
        var loginBody = await login.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("no-store", string.Join(";", login.Headers.CacheControl?.ToString() ?? string.Empty), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rel=\"manifest\"", loginBody, StringComparison.Ordinal);
        Assert.Contains("src=\"/js/site.js", loginBody, StringComparison.Ordinal);
        Assert.Contains("data-wms-connectivity", loginBody, StringComparison.Ordinal);
    }
}
