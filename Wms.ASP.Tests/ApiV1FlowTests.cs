using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.ApiClients;
using Wms.Application.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ApiV1FlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task VersionMetadataAndOpenApiDocumentExposeThePublishedBoundary()
    {
        using var client = CreateClient();

        using var metadata = await client.GetAsync("/api/v1");
        var metadataBody = await metadata.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        using var metadataDocument = JsonDocument.Parse(metadataBody);
        Assert.Equal("v1", metadataDocument.RootElement.GetProperty("version").GetString());
        Assert.Equal("foundation", metadataDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, metadataDocument.RootElement.GetProperty("queryResources").GetArrayLength());
        Assert.Equal(0, metadataDocument.RootElement.GetProperty("commandResources").GetArrayLength());

        using var openApi = await client.GetAsync("/api/v1/openapi.json");
        var openApiBody = await openApi.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        using var openApiDocument = JsonDocument.Parse(openApiBody);
        Assert.Equal("3.1.0", openApiDocument.RootElement.GetProperty("openapi").GetString());
        var paths = openApiDocument.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/items", out _));
        Assert.True(paths.TryGetProperty("/api/v1/inventory/stock", out _));
        Assert.True(paths.TryGetProperty("/api/v1/reports/movements", out _));
        Assert.True(openApiDocument.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .TryGetProperty("ProblemDetails", out _));
        var securitySchemes = openApiDocument.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");
        Assert.True(securitySchemes.TryGetProperty("apiClientAuth", out _));
        Assert.False(securitySchemes.TryGetProperty("cookieAuth", out _));
    }

    [Fact]
    public async Task AuthorizedQueriesReturnBoundedApplicationOwnedPages()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        await factory.CreateWarehouseScenarioAsync(administrator.Id);
        var issue = await IssueClientAsync(
            administrator.Id,
            true,
            WmsPermissions.WarehouseManage,
            WmsPermissions.ItemsRead,
            WmsPermissions.LocationsRead,
            WmsPermissions.InventoryRead,
            WmsPermissions.ReportsRead);

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            issue.Client.ClientId + "." + issue.Secret);

        foreach (var path in new[]
                 {
                     "/api/v1/warehouses?page=1&pageSize=1",
                     "/api/v1/items?page=1&pageSize=1",
                     "/api/v1/locations?page=1&pageSize=1",
                     "/api/v1/inventory/stock?page=1&pageSize=1",
                     "/api/v1/reports/movements?page=1&pageSize=1"
                 })
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Assert.True(root.TryGetProperty("items", out _), path);
            Assert.True(root.TryGetProperty("page", out _), path);
            Assert.True(root.TryGetProperty("pageSize", out _), path);
            Assert.True(root.TryGetProperty("totalCount", out _), path);
            Assert.True(root.TryGetProperty("totalPages", out _), path);
            Assert.DoesNotContain("EntityFramework", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("WmsDbContext", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task VersionedResourcesRejectUnboundedPageSizes()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        var issue = await IssueClientAsync(
            administrator.Id,
            true,
            WmsPermissions.ItemsRead);

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            issue.Client.ClientId + "." + issue.Secret);

        using var response = await client.GetAsync("/api/v1/items?page=1&pageSize=201");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("api.invalid_paging", body, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType?.MediaType ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HumanSessionCookiesAreNotAcceptedAsVersionedApiCredentials()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);

        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            administrator.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/api/v1/items");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ApiClientIssue> IssueClientAsync(
        string actorUserId,
        bool hasGlobalWarehouseAccess,
        params string[] scopes)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IApiClientCredentialService>();
        var result = await service.CreateAsync(
            new ApiClientCreateRequest(
                "API v1 test client",
                "integration-tests",
                scopes,
                HasGlobalWarehouseAccess: hasGlobalWarehouseAccess),
            actorUserId);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value;
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
