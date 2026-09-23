using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class WarehouseWorkContractFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task Complete_requires_the_work_execute_permission_even_when_the_user_can_read_work()
    {
        var auditor = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(auditor.Id, WmsRoleNames.Auditor);
        await factory.CreateWarehouseScenarioAsync(auditor.Id);

        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            auditor.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var page = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var tokenMatch = Regex.Match(
            await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(tokenMatch.Success);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/work/999999/complete")
        {
            Content = JsonContent.Create(new { idempotencyKey = "unauthorized-completion", lines = Array.Empty<object>() })
        };
        request.Headers.Add("RequestVerificationToken", WebUtility.HtmlDecode(tokenMatch.Groups[1].Value));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_putaway_completion_returns_conflict_and_rolls_back_inventory_mutation()
    {
        var staff = await factory.CreateUserAsync();
        var scenario = await CreateOpenPutawayScenarioAsync(staff.Id);

        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            staff.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var page = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var tokenMatch = Regex.Match(
            await page.Content.ReadAsStringAsync(),
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(tokenMatch.Success);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/work/{scenario.WorkId}/complete")
        {
            Content = JsonContent.Create(new
            {
                idempotencyKey = "complete-open-work",
                scans = new[]
                {
                    new
                    {
                        lineId = scenario.LineId,
                        itemId = scenario.ItemId,
                        sourceLocationId = scenario.SourceLocationId,
                        destinationLocationId = scenario.DestinationLocationId,
                        actualQuantity = 5m
                    }
                }
            })
        };
        request.Headers.Add("RequestVerificationToken", WebUtility.HtmlDecode(tokenMatch.Groups[1].Value));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(body);
        Assert.Equal(409, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("work.completion_invalid", problem.RootElement.GetProperty("errorCode").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var work = await context.WarehouseWorks.SingleAsync(value => value.Id == scenario.WorkId);
        Assert.Equal(WarehouseWorkStatus.Open, work.Status);
        Assert.Empty(await context.Movements.ToListAsync());
        var source = await context.Stock.SingleAsync(value => value.LocationId == scenario.SourceLocationId);
        Assert.Equal(10m, source.QuantityAvailable.Value);
        Assert.False(await context.Stock.AnyAsync(value => value.LocationId == scenario.DestinationLocationId));
    }

    private async Task<OpenPutawayScenario> CreateOpenPutawayScenarioAsync(string userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var token = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var warehouse = new Warehouse("WW" + token, "Work contract warehouse");
        var item = new Item("WW-ITEM-" + token, "Work contract item", "EA");
        context.AddRange(warehouse, item);
        await context.SaveChangesAsync();

        var source = new Location(
            "WW-SRC-" + token,
            "Work contract receiving",
            warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false);
        var destination = new Location(
            "WW-DST-" + token,
            "Work contract storage",
            warehouse.Id,
            type: LocationType.Storage);
        context.AddRange(source, destination);
        await context.SaveChangesAsync();

        context.Stock.Add(new Stock(item.Id, source.Id, new Quantity(10m)));
        await context.SaveChangesAsync();

        context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
        {
            UserId = userId,
            WarehouseId = warehouse.Id,
            IsDefault = true
        });

        var work = new WarehouseWork(
            "WW-WORK-" + token,
            "WW-CREATE-" + token,
            WarehouseWorkType.Putaway,
            warehouse.Id,
            "RECEIPT",
            "WW-RECEIPT-" + token);
        work.AddLine(new WarehouseWorkLine(
            1,
            warehouse.Id,
            item.Id,
            5m,
            "EA",
            source.Id,
            destination.Id));
        context.WarehouseWorks.Add(work);
        await context.SaveChangesAsync();

        return new OpenPutawayScenario(
            work.Id,
            work.Lines.Single().Id,
            item.Id,
            source.Id,
            destination.Id);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private sealed record OpenPutawayScenario(
        int WorkId,
        int LineId,
        int ItemId,
        int SourceLocationId,
        int DestinationLocationId);
}
