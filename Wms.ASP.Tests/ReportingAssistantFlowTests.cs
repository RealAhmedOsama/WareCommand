using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ReportingAssistantFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ExecutorReturnsPersistedReceiptOrderInventoryAndMovementFacts()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(user.Id, WmsRoleNames.WarehouseManager);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        var warehouse = await GetWarehouseScenarioAsync(scenario.AssignedLocationCode);
        var customer = await CreateCustomerAsync();
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var receivingToken = await GetAntiforgeryTokenAsync(client, "/Receiving/Receive");
        using var receipt = await client.PostAsync(
            "/Receiving/Receive",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ItemSku"] = scenario.ItemSku,
                ["LocationCode"] = scenario.AssignedLocationCode,
                ["Quantity"] = "5",
                ["UnitOfMeasure"] = "EA",
                ["ReferenceNumber"] = "ASSISTANT-RECEIPT",
                ["__RequestVerificationToken"] = receivingToken
            }));
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
        var receiptNumber = await GetLatestReceiptNumberAsync(warehouse.WarehouseId);
        var token = await GetAntiforgeryTokenAsync(client);

        using var orderCreate = await PostJsonAsync(
            client,
            "/api/sales-orders",
            token,
            new
            {
                warehouseId = warehouse.WarehouseId,
                customerId = customer.Id,
                orderDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                lines = new[]
                {
                    new { itemSku = scenario.ItemSku, orderedQuantity = 3, unitOfMeasure = "EA" }
                }
            });
        var orderBody = await orderCreate.Content.ReadAsStringAsync();
        Assert.True(orderCreate.IsSuccessStatusCode, orderBody);
        using var createdOrder = JsonDocument.Parse(orderBody);
        var orderNumber = createdOrder.RootElement.GetProperty("documentNumber").GetString()!;

        using var receiving = await QueryAssistantAsync(client, token, "Show receipt status", warehouse.WarehouseId);
        using var receivingJson = await ReadSuccessJsonAsync(receiving);
        Assert.Equal("Answered", receivingJson.RootElement.GetProperty("status").GetString());
        Assert.Contains(receiptNumber, ReadTextValues(receivingJson.RootElement));
        Assert.Contains(
            "receipts.status.read",
            receivingJson.RootElement.GetProperty("citations").EnumerateArray()
                .Select(citation => citation.GetProperty("sourceTool").GetString()));

        using var outbound = await QueryAssistantAsync(client, token, "Show order status", warehouse.WarehouseId);
        using var outboundJson = await ReadSuccessJsonAsync(outbound);
        Assert.Equal("Answered", outboundJson.RootElement.GetProperty("status").GetString());
        Assert.Contains(orderNumber, ReadTextValues(outboundJson.RootElement));
        Assert.Contains(
            "sales-orders.status.read",
            outboundJson.RootElement.GetProperty("citations").EnumerateArray()
                .Select(citation => citation.GetProperty("sourceTool").GetString()));

        using var inventory = await QueryAssistantAsync(client, token, "Show stock", warehouse.WarehouseId);
        using var inventoryJson = await ReadSuccessJsonAsync(inventory);
        Assert.Equal("Answered", inventoryJson.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            inventoryJson.RootElement.GetProperty("data").EnumerateArray()
                .SelectMany(row => row.GetProperty("fields").EnumerateArray()),
            field => field.GetProperty("name").GetString() == "availableQuantity" &&
                     field.GetProperty("numberValue").GetDecimal() == 15m &&
                     field.GetProperty("unit").GetString() == "base unit");

        using var movement = await QueryAssistantAsync(client, token, "Show movement ledger", warehouse.WarehouseId);
        using var movementJson = await ReadSuccessJsonAsync(movement);
        Assert.Equal("Answered", movementJson.RootElement.GetProperty("status").GetString());
        Assert.NotEmpty(movementJson.RootElement.GetProperty("data").EnumerateArray());
        var cutoff = movementJson.RootElement.GetProperty("dataCutoffUtc").GetDateTimeOffset();
        Assert.All(
            movementJson.RootElement.GetProperty("citations").EnumerateArray(),
            citation => Assert.Equal(cutoff, citation.GetProperty("dataCutoffUtc").GetDateTimeOffset()));
        Assert.All(
            movementJson.RootElement.GetProperty("citations").EnumerateArray(),
            citation => Assert.StartsWith("movement:", citation.GetProperty("reference").GetString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecutorRechecksPermissionAndWarehouseScopeAndReturnsStatelessClarification()
    {
        var user = await factory.CreateUserAsync(assignDefaultRole: false);
        var permissionRole = await AssignPermissionRoleAsync(
            user.Id,
            WmsPermissions.ReportsRead,
            WmsPermissions.InventoryRead,
            WmsPermissions.SalesOrdersRead);
        var scenario = await factory.CreateWarehouseScenarioAsync(user.Id);
        var warehouse = await GetWarehouseScenarioAsync(scenario.AssignedLocationCode);
        var unassignedWarehouseId = await GetWarehouseIdAsync(scenario.UnassignedLocationCode);
        using var client = CreateClient();
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = await GetAntiforgeryTokenAsync(client);

        using var clarification = await QueryAssistantAsync(
            client,
            token,
            "Show inventory movements",
            warehouse.WarehouseId);
        using var clarificationJson = await ReadSuccessJsonAsync(clarification);
        Assert.Equal("NeedsClarification", clarificationJson.RootElement.GetProperty("status").GetString());
        Assert.Empty(clarificationJson.RootElement.GetProperty("data").EnumerateArray());
        Assert.Empty(clarificationJson.RootElement.GetProperty("citations").EnumerateArray());

        using var crossWarehouse = await QueryAssistantAsync(
            client,
            token,
            "Show stock",
            unassignedWarehouseId);
        Assert.Equal(HttpStatusCode.Forbidden, crossWarehouse.StatusCode);

        using var arabic = await QueryAssistantAsync(
            client,
            token,
            "اعرض رصيد المخزون",
            warehouse.WarehouseId,
            "ar-SA");
        using var arabicJson = await ReadSuccessJsonAsync(arabic);
        Assert.Equal("Answered", arabicJson.RootElement.GetProperty("status").GetString());

        using var empty = await QueryAssistantAsync(
            client,
            token,
            "Show order status",
            warehouse.WarehouseId);
        using var emptyJson = await ReadSuccessJsonAsync(empty);
        Assert.Equal("Empty", emptyJson.RootElement.GetProperty("status").GetString());
        Assert.Empty(emptyJson.RootElement.GetProperty("data").EnumerateArray());

        using var mutation = await QueryAssistantAsync(
            client,
            token,
            "Delete stock",
            warehouse.WarehouseId);
        using var mutationJson = await ReadSuccessJsonAsync(mutation);
        Assert.Equal("Unsupported", mutationJson.RootElement.GetProperty("status").GetString());
        Assert.Empty(mutationJson.RootElement.GetProperty("citations").EnumerateArray());

        using var sqlInjection = await QueryAssistantAsync(
            client,
            token,
            "Show stock; SELECT * FROM users",
            warehouse.WarehouseId);
        using var injectionJson = await ReadSuccessJsonAsync(sqlInjection);
        Assert.Equal("Unsupported", injectionJson.RootElement.GetProperty("status").GetString());
        Assert.Empty(injectionJson.RootElement.GetProperty("data").EnumerateArray());

        using var inventoryAfterRejectedCommands = await QueryAssistantAsync(
            client,
            token,
            "Show stock",
            warehouse.WarehouseId);
        using var inventoryAfterRejectedCommandsJson = await ReadSuccessJsonAsync(inventoryAfterRejectedCommands);
        Assert.Contains(
            inventoryAfterRejectedCommandsJson.RootElement.GetProperty("data").EnumerateArray()
                .SelectMany(row => row.GetProperty("fields").EnumerateArray()),
            field => field.GetProperty("name").GetString() == "availableQuantity" &&
                     field.GetProperty("numberValue").GetDecimal() == 10m);

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            await context.RoleClaims
                .Where(claim => claim.RoleId == permissionRole.Id &&
                                claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                                claim.ClaimValue == WmsPermissions.InventoryRead)
                .ExecuteDeleteAsync();
        }

        using var revoked = await PostJsonAsync(
            client,
            "/api/reporting-assistant/query",
            token,
            new
            {
                question = "Show stock",
                locale = "en-US",
                warehouseId = warehouse.WarehouseId,
                maximumRows = 25,
                permissionCodes = new[] { WmsPermissions.All },
                allowedWarehouseIds = new[] { warehouse.WarehouseId }
            });
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Fact]
    public async Task HttpEndpointUsesTheConfiguredReportRateLimit()
    {
        using var limitedFactory = new WareCommandWebApplicationFactory
        {
            ReportPermitLimitOverride = 1
        };
        var user = await limitedFactory.CreateUserAsync(assignDefaultRole: false);
        await limitedFactory.AssignRoleAsync(user.Id, WmsRoleNames.WarehouseManager);
        var scenario = await limitedFactory.CreateWarehouseScenarioAsync(user.Id);
        var warehouseId = await GetWarehouseIdAsync(limitedFactory, scenario.AssignedLocationCode);
        using var client = CreateClient(limitedFactory);
        using var login = await AuthenticationFlowTests.PostLoginAsync(
            client,
            user.UserName!,
            "ValidPassword123!");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var token = await GetAntiforgeryTokenAsync(client, "/Receiving/Receive");

        using var allowed = await QueryAssistantAsync(client, token, "Show stock", warehouseId);
        using var allowedJson = await ReadSuccessJsonAsync(allowed);
        Assert.Equal("Answered", allowedJson.RootElement.GetProperty("status").GetString());

        using var rejected = await QueryAssistantAsync(client, token, "Show stock", warehouseId);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("Too many requests", await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private HttpClient CreateClient(WareCommandWebApplicationFactory? targetFactory = null) =>
        (targetFactory ?? factory).CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<string> GetAntiforgeryTokenAsync(
        HttpClient client,
        string path = "/Reports?culture=en-US&ui-culture=en-US")
    {
        using var page = await client.GetAsync(path);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Could not find an antiforgery token on the reports page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> QueryAssistantAsync(
        HttpClient client,
        string antiforgeryToken,
        string question,
        int warehouseId,
        string locale = "en-US") => await PostJsonAsync(
        client,
        "/api/reporting-assistant/query",
        antiforgeryToken,
        new { question, locale, warehouseId, maximumRows = 25 });

    private static async Task<HttpResponseMessage> PostJsonAsync(
        HttpClient client,
        string path,
        string antiforgeryToken,
        object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("RequestVerificationToken", antiforgeryToken);
        return await client.SendAsync(request);
    }

    private static async Task<JsonDocument> ReadSuccessJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected HTTP 200, got {(int)response.StatusCode}; response body starts with: {body[..Math.Min(body.Length, 300)]}");
        return JsonDocument.Parse(body);
    }

    private static IEnumerable<string?> ReadTextValues(JsonElement answer) =>
        answer.GetProperty("data").EnumerateArray()
            .SelectMany(row => row.GetProperty("fields").EnumerateArray())
            .Where(field => field.GetProperty("textValue").ValueKind == JsonValueKind.String)
            .Select(field => field.GetProperty("textValue").GetString());

    private async Task<(int WarehouseId, int LocationId)> GetWarehouseScenarioAsync(string locationCode)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        return await context.Locations
            .Where(location => location.Code == locationCode)
            .Select(location => new ValueTuple<int, int>(location.WarehouseId, location.Id))
            .SingleAsync();
    }

    private Task<int> GetWarehouseIdAsync(string locationCode) => GetWarehouseIdAsync(factory, locationCode);

    private static async Task<int> GetWarehouseIdAsync(
        WareCommandWebApplicationFactory sourceFactory,
        string locationCode)
    {
        using var scope = sourceFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        return await context.Locations
            .Where(location => location.Code == locationCode)
            .Select(location => location.WarehouseId)
            .SingleAsync();
    }

    private async Task<Customer> CreateCustomerAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        var customer = new Customer(
            "RPT-" + Guid.NewGuid().ToString("N")[..8],
            "Reporting Assistant Test Customer");
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer;
    }

    private async Task<IdentityRole> AssignPermissionRoleAsync(
        string userId,
        params string[] permissions)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var role = new IdentityRole("ReportingAssistant-" + Guid.NewGuid().ToString("N")[..8]);
        var createdRole = await roleManager.CreateAsync(role);
        Assert.True(createdRole.Succeeded, string.Join("; ", createdRole.Errors.Select(error => error.Description)));
        foreach (var permission in permissions)
        {
            var claimResult = await roleManager.AddClaimAsync(
                role,
                new Claim(WmsAuthorizationClaimTypes.Permission, permission));
            Assert.True(claimResult.Succeeded, string.Join("; ", claimResult.Errors.Select(error => error.Description)));
        }

        var user = await userManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException("The reporting assistant test user was not found.");
        var assigned = await userManager.AddToRoleAsync(user, role.Name!);
        Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(error => error.Description)));
        return role;
    }

    private async Task<string> GetLatestReceiptNumberAsync(int warehouseId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
        return await context.Receipts
            .Where(receipt => receipt.WarehouseId == warehouseId)
            .OrderByDescending(receipt => receipt.CreatedAt)
            .Select(receipt => receipt.DocumentNumber)
            .FirstAsync();
    }
}
