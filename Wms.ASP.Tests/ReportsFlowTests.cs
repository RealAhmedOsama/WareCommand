using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ReportsFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task MovementReportCombinesFiltersPagesAndPreservesWarehouseScope()
    {
        var user = await factory.CreateUserAsync();
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.WarehouseManager);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        await SeedMovementsAsync(scenario, user.Id);

        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var filtered = await client.GetAsync(
            $"/Reports?fromDate=2026-09-20&toDate=2026-09-20&itemSku={Uri.EscapeDataString(scenario.ItemSku)}" +
            $"&locationCode={Uri.EscapeDataString(scenario.AssignedLocationCode)}&movementType=Receipt" +
            "&searchTerm=REF-ALPHA&pageSize=25&culture=en-US&ui-culture=en-US");
        var filteredBody = await filtered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Contains("Movement ledger", filteredBody, StringComparison.Ordinal);
        Assert.Contains("REF-ALPHA", filteredBody, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-BETA", filteredBody, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-OTHER", filteredBody, StringComparison.Ordinal);

        using var paged = await client.GetAsync(
            $"/Reports?fromDate=2026-09-20&toDate=2026-09-21&itemSku={Uri.EscapeDataString(scenario.ItemSku)}" +
            "&pageSize=1&page=2&culture=en-US&ui-culture=en-US");
        var pagedBody = await paged.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, paged.StatusCode);
        Assert.Contains("2 / 2", pagedBody, StringComparison.Ordinal);
        Assert.Contains("REF-ALPHA", pagedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-OTHER", pagedBody, StringComparison.Ordinal);

        using var inaccessibleWarehouse = await client.GetAsync(
            "/Reports?warehouseId=999999&fromDate=2026-09-20&toDate=2026-09-21&culture=en-US&ui-culture=en-US");
        var inaccessibleBody = await inaccessibleWarehouse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, inaccessibleWarehouse.StatusCode);
        Assert.DoesNotContain("REF-ALPHA", inaccessibleBody, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-OTHER", inaccessibleBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MovementReportExportsUtf8CsvAndRendersArabicRtl()
    {
        var user = await factory.CreateUserAsync();
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.WarehouseManager);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        await SeedMovementsAsync(scenario, user.Id);

        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var export = await client.GetAsync(
            $"/Reports/Export?fromDate=2026-09-20&toDate=2026-09-21&itemSku={Uri.EscapeDataString(scenario.ItemSku)}");
        var exportBytes = await export.Content.ReadAsByteArrayAsync();
        var exportBody = Encoding.UTF8.GetString(exportBytes);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.StartsWith("\uFEFFType,SKU", exportBody, StringComparison.Ordinal);
        Assert.Contains("REF-ALPHA", exportBody, StringComparison.Ordinal);
        Assert.Contains("REF-BETA", exportBody, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-OTHER", exportBody, StringComparison.Ordinal);
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);

        using var arabic = await client.GetAsync(
            $"/Reports?fromDate=2026-09-20&toDate=2026-09-21&itemSku={Uri.EscapeDataString(scenario.ItemSku)}" +
            "&culture=ar-SA&ui-culture=ar-SA");
        var arabicBody = await arabic.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, arabic.StatusCode);
        Assert.Contains("<html lang=\"ar-SA\" dir=\"rtl\">", arabicBody, StringComparison.Ordinal);
        Assert.Contains("سجل حركات المخزون", WebUtility.HtmlDecode(arabicBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GroupedMovementReportAggregatesOnTheServerAndClampsOutOfRangePages()
    {
        var user = await factory.CreateUserAsync();
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.WarehouseManager);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        await SeedMovementsAsync(scenario, user.Id);

        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync(
            $"/Reports?fromDate=2026-09-20&toDate=2026-09-21&itemSku={Uri.EscapeDataString(scenario.ItemSku)}" +
            "&groupBy=Item&pageSize=1&page=99&culture=en-US&ui-culture=en-US");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(scenario.ItemSku, body, StringComparison.Ordinal);
        Assert.Contains("8.00", body, StringComparison.Ordinal);
        Assert.DoesNotContain("REF-OTHER", body, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private async Task SeedMovementsAsync(WarehouseTestData scenario, string userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var item = await context.Items.SingleAsync(item => item.Sku == scenario.ItemSku);
        var assignedLocation = await context.Locations.SingleAsync(
            location => location.Code == scenario.AssignedLocationCode);
        var otherLocation = await context.Locations.SingleAsync(
            location => location.Code == scenario.UnassignedLocationCode);

        context.Movements.AddRange(
            Movement.CreateReceipt(
                item.Id,
                assignedLocation.Id,
                new Quantity(5),
                userId,
                referenceNumber: "REF-ALPHA",
                timestampUtc: new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc)),
            Movement.CreatePick(
                item.Id,
                assignedLocation.Id,
                new Quantity(3),
                userId,
                referenceNumber: "REF-BETA",
                timestampUtc: new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc)),
            Movement.CreateReceipt(
                item.Id,
                otherLocation.Id,
                new Quantity(99),
                userId,
                referenceNumber: "REF-OTHER",
                timestampUtc: new DateTime(2026, 9, 20, 11, 0, 0, DateTimeKind.Utc)));
        await context.SaveChangesAsync();
    }
}
