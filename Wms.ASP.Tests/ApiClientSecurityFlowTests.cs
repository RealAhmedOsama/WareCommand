using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.ApiClients;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ApiClientSecurityFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task ApiClientSecretsAreHashedRotatableAndWarehouseScoped()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        var scenario = await factory.CreateWarehouseScenarioAsync(administrator.Id);

        int assignedWarehouseId;
        int unassignedWarehouseId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            assignedWarehouseId = await context.Locations
                .Where(location => location.Code == scenario.AssignedLocationCode)
                .Select(location => location.WarehouseId)
                .SingleAsync();
            unassignedWarehouseId = await context.Locations
                .Where(location => location.Code == scenario.UnassignedLocationCode)
                .Select(location => location.WarehouseId)
                .SingleAsync();
        }

        ApiClientIssue issue;
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IApiClientCredentialService>();
            var result = await service.CreateAsync(
                new ApiClientCreateRequest(
                    "Scoped integration client",
                    "integration-tests",
                    [WmsPermissions.InventoryRead],
                    [assignedWarehouseId]),
                administrator.Id);
            Assert.True(result.IsSuccess, result.Error);
            issue = result.Value;

            var entity = await scope.ServiceProvider
                .GetRequiredService<WmsDbContext>()
                .ApiClients
                .SingleAsync(client => client.ClientId == issue.Client.ClientId);
            Assert.DoesNotContain(issue.Secret, entity.SecretHash, StringComparison.Ordinal);
            Assert.NotEqual(issue.Secret, entity.SecretHash);

            var verified = await service.VerifyAsync(
                issue.Client.ClientId,
                issue.Secret,
                "127.0.0.1");
            Assert.True(verified.IsSuccess, verified.Error);

            var rejected = await service.VerifyAsync(
                issue.Client.ClientId,
                issue.Secret + "-wrong",
                "127.0.0.1");
            Assert.True(rejected.IsFailure);
            Assert.Equal("api_client.invalid_credentials", rejected.ErrorCode);

            var rotated = await service.RotateAsync(
                issue.Client.ClientId,
                new ApiClientRotateRequest(TimeSpan.FromMinutes(10)),
                administrator.Id);
            Assert.True(rotated.IsSuccess, rotated.Error);
            var rotatedIssue = rotated.Value;

            var overlap = await service.VerifyAsync(
                issue.Client.ClientId,
                issue.Secret,
                "127.0.0.1");
            Assert.True(overlap.IsSuccess, overlap.Error);
            var newSecret = await service.VerifyAsync(
                issue.Client.ClientId,
                rotatedIssue.Secret,
                "127.0.0.1");
            Assert.True(newSecret.IsSuccess, newSecret.Error);

            using (var scopedClient = CreateClient())
            {
                scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    rotatedIssue.Client.ClientId + "." + rotatedIssue.Secret);
                using var assignedResponse = await scopedClient.GetAsync(
                    $"/api/v1/inventory/stock?warehouseId={assignedWarehouseId}");
                Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
                using var unassignedResponse = await scopedClient.GetAsync(
                    $"/api/v1/inventory/stock?warehouseId={unassignedWarehouseId}");
                Assert.Equal(HttpStatusCode.Forbidden, unassignedResponse.StatusCode);
            }

            var revoked = await service.RevokeAsync(
                issue.Client.ClientId,
                administrator.Id);
            Assert.True(revoked.IsSuccess, revoked.Error);
            var afterRevoke = await service.VerifyAsync(
                issue.Client.ClientId,
                rotatedIssue.Secret,
                "127.0.0.1");
            Assert.True(afterRevoke.IsFailure);

            issue = rotatedIssue;
        }

        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            issue.Client.ClientId + "." + issue.Secret);
        using var revokedResponse = await client.GetAsync(
            $"/api/v1/inventory/stock?warehouseId={assignedWarehouseId}");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResponse.StatusCode);

    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}
