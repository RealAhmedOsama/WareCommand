using System.Globalization;
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

public sealed class LocationManagementFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task WarehouseStaffCanReadLocationsButCannotManageThem()
    {
        var user = await factory.CreateUserAsync();
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var read = await client.GetAsync("/Locations");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var denied = await client.GetAsync("/Locations/Create");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task AdministratorCanCreateTypedLocationAndSeeServerSideManagementSurface()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.Administrator);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var warehouseId = await context.Locations
            .Where(location => location.Code == scenario.AssignedLocationCode)
            .Select(location => location.WarehouseId)
            .SingleAsync();

        using var index = await client.GetAsync($"/Locations?warehouseId={warehouseId}&pageSize=1");
        var indexBody = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains(scenario.AssignedLocationCode, indexBody, StringComparison.Ordinal);
        Assert.Contains("pageSize=1", indexBody, StringComparison.Ordinal);

        using var createPage = await client.GetAsync("/Locations/Create");
        var createBody = await createPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var token = ExtractAntiForgeryToken(createBody);
        var code = "BIN-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        using var create = await client.PostAsync(
            "/Locations/Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Code"] = code,
                ["Name"] = "Web bin",
                ["WarehouseId"] = warehouseId.ToString(CultureInfo.InvariantCulture),
                ["Type"] = nameof(LocationType.Bin),
                ["Priority"] = "3",
                ["IsPickable"] = "true",
                ["IsReceivable"] = "false",
                ["IsCountable"] = "true",
                ["AllowMixedItems"] = "true",
                ["AllowMixedLots"] = "true",
                ["MaxUnits"] = "25",
                ["ConstraintAttributesJson"] = "{}",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.StartsWith("/Locations/Details", create.Headers.Location?.OriginalString, StringComparison.Ordinal);

        var location = await context.Locations.SingleAsync(item => item.Code == code);
        Assert.Equal(LocationType.Bin, location.Type);
        Assert.Equal(25m, location.MaxUnits);
        Assert.True(await context.AuditEntries.AnyAsync(entry =>
            entry.Action == WmsAuditActions.LocationCreated && entry.EntityId == code));

        using var details = await client.GetAsync($"/Locations/Details?id={location.Id}");
        var detailsBody = await details.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Contains(code, detailsBody, StringComparison.Ordinal);
        Assert.Contains("Capacity and constraints", detailsBody, StringComparison.Ordinal);
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
