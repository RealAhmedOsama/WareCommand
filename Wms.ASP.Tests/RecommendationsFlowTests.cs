using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class RecommendationsFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task Authorized_recommendation_routes_are_composed_and_disabled_generation_is_safe()
    {
        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
        await factory.CreateWarehouseScenarioAsync(administrator.Id);
        int warehouseId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            warehouseId = await context.UserWarehouseAssignments
                .Where(assignment => assignment.UserId == administrator.Id)
                .Select(assignment => assignment.WarehouseId)
                .SingleAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            administrator.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var list = await client.GetAsync("/api/recommendations");
        Assert.True(list.StatusCode == HttpStatusCode.OK,
            $"Expected recommendations to list successfully; body: {await list.Content.ReadAsStringAsync()}");

        using var page = await client.GetAsync("/Items");
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Could not read the antiforgery token from the authenticated page.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/recommendations/replenishment/generate")
        {
            Content = JsonContent.Create(new { warehouseId, limit = 10 })
        };
        request.Headers.TryAddWithoutValidation(
            "RequestVerificationToken",
            WebUtility.HtmlDecode(match.Groups[1].Value));
        using var generate = await client.SendAsync(request);
        var body = await generate.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, generate.StatusCode);
        Assert.Contains("recommendation.disabled", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recommendation_review_and_command_routes_require_authentication()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var review = await client.PostAsync(
            "/api/recommendations/" + new string('a', 64) + "/review",
            JsonContent.Create(new { expectedRevision = 1, idempotencyKey = "review-unauthenticated" }));
        using var execute = await client.PostAsync(
            "/api/recommendations/" + new string('a', 64) + "/execute",
            JsonContent.Create(new { expectedRevision = 1, idempotencyKey = "execute-unauthenticated" }));

        Assert.Equal(HttpStatusCode.Unauthorized, review.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, execute.StatusCode);
    }
}
