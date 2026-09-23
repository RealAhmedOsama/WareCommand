using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wms.Application.AnomalyDetection;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlDashboardFlowTests
{
    private static readonly JsonSerializerOptions AssistantJsonOptions = new(JsonSerializerDefaults.Web);

    [PostgreSqlDashboardFact]
    public async Task DashboardPageAndRefreshUsePersistedPostgreSqlReceiptData()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            "WARECOMMAND_TEST_POSTGRES_CONNECTION")!;
        var factory = new PostgreSqlDashboardApplicationFactory(baseConnectionString);
        try
        {
            await factory.CreateIsolatedDatabaseAsync();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });

            var user = await factory.CreateUserAsync();
            var scenario = await factory.CreateScenarioAsync(user.Id);
            using var login = await AuthenticationFlowTests.PostLoginAsync(
                client,
                user.UserName!,
                "ValidPassword123!");
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

            using var before = await client.GetAsync("/Dashboard/RefreshData");
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            using var beforeSnapshot = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
            Assert.Equal(
                10m,
                beforeSnapshot.RootElement.GetProperty("inventory").GetProperty("data")
                    .GetProperty("onHandQuantity").GetDecimal());
            Assert.Equal("America/Chicago", beforeSnapshot.RootElement.GetProperty("timeZoneId").GetString());

            using var receivePage = await client.GetAsync("/Receiving/Receive");
            var receiveHtml = await receivePage.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, receivePage.StatusCode);
            var tokenMatch = Regex.Match(
                receiveHtml,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.True(tokenMatch.Success, "Could not find the receiving antiforgery token.");

            using var receipt = await client.PostAsync(
                "/Receiving/Receive",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["ItemSku"] = scenario.ItemSku,
                    ["LocationCode"] = scenario.LocationCode,
                    ["Quantity"] = "5",
                    ["UnitOfMeasure"] = "EA",
                    ["ReferenceNumber"] = "PG-DASHBOARD-RECEIPT",
                    ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
                }));
            Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);

            using var after = await client.GetAsync("/Dashboard/RefreshData");
            Assert.Equal(HttpStatusCode.OK, after.StatusCode);
            using var afterSnapshot = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
            Assert.Equal(
                15m,
                afterSnapshot.RootElement.GetProperty("inventory").GetProperty("data")
                    .GetProperty("onHandQuantity").GetDecimal());
            Assert.Equal("Available", afterSnapshot.RootElement.GetProperty("recentMovements")
                .GetProperty("status").GetString());
            var recentMovements = afterSnapshot.RootElement.GetProperty("recentMovements")
                .GetProperty("data");
            Assert.NotEmpty(recentMovements.EnumerateArray());
            Assert.Equal("Receipt", recentMovements[0].GetProperty("type").GetString());
            Assert.Equal(5m, recentMovements[0].GetProperty("quantity").GetDecimal());

            using var reportsPage = await client.GetAsync("/Reports?culture=en-US&ui-culture=en-US");
            var reportsHtml = await reportsPage.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, reportsPage.StatusCode);
            var reportToken = Regex.Match(
                reportsHtml,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.True(reportToken.Success, "Could not find an antiforgery token on the PostgreSQL reports page.");
            using var assistantRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/reporting-assistant/query")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        question = "Show movement ledger",
                        locale = "en-US",
                        maximumRows = 25
                    }, AssistantJsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            assistantRequest.Headers.TryAddWithoutValidation(
                "RequestVerificationToken",
                WebUtility.HtmlDecode(reportToken.Groups[1].Value));
            using var assistantResponse = await client.SendAsync(assistantRequest);
            var assistantBody = await assistantResponse.Content.ReadAsStringAsync();
            Assert.True(
                assistantResponse.StatusCode == HttpStatusCode.OK,
                $"PostgreSQL reporting assistant returned {(int)assistantResponse.StatusCode}: {assistantBody[..Math.Min(assistantBody.Length, 300)]}");
            using var assistantSnapshot = JsonDocument.Parse(assistantBody);
            Assert.Equal("Answered", assistantSnapshot.RootElement.GetProperty("status").GetString());
            Assert.NotEmpty(assistantSnapshot.RootElement.GetProperty("data").EnumerateArray());
            Assert.All(
                assistantSnapshot.RootElement.GetProperty("citations").EnumerateArray(),
                citation =>
                {
                    Assert.Equal("reports.movements.read", citation.GetProperty("sourceTool").GetString());
                    Assert.StartsWith("movement:", citation.GetProperty("reference").GetString(), StringComparison.Ordinal);
                    Assert.True(citation.GetProperty("dataCutoffUtc").GetDateTimeOffset() > DateTimeOffset.MinValue);
                });

            using (var scope = factory.Services.CreateScope())
            {
                var forecasting = scope.ServiceProvider.GetRequiredService<IForecastingService>();
                var recalculated = await forecasting.RecalculateAsync(new ForecastingRecalculationQuery(
                    scenario.WarehouseId,
                    scenario.ItemId,
                    ForecastGranularity.Daily,
                    HorizonPeriods: 3));
                Assert.True(recalculated.IsSuccess, recalculated.Error);
                Assert.Equal(1, recalculated.Value.RunsCreated);

                var anomalies = scope.ServiceProvider.GetRequiredService<IAnomalyDetectionService>();
                var anomalyToUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
                var anomalyFromUtc = anomalyToUtc.AddDays(-7);
                var anomalyRun = await anomalies.RecalculateAsync(new AnomalyRecalculationInput(
                    scenario.WarehouseId,
                    anomalyFromUtc,
                    anomalyToUtc));
                var repeatedAnomalyRun = await anomalies.RecalculateAsync(new AnomalyRecalculationInput(
                    scenario.WarehouseId,
                    anomalyFromUtc,
                    anomalyToUtc));
                Assert.True(anomalyRun.IsSuccess, anomalyRun.Error);
                Assert.True(repeatedAnomalyRun.IsSuccess, repeatedAnomalyRun.Error);
                Assert.False(anomalyRun.Value.WasReused);
                Assert.True(repeatedAnomalyRun.Value.WasReused);

                var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
                var sourceRow = await context.InventoryTransactions.AsNoTracking()
                    .Where(row => row.WarehouseId == scenario.WarehouseId)
                    .OrderByDescending(row => row.Id)
                    .FirstAsync();
                var key = new InventoryBalanceKey(
                    sourceRow.WarehouseId,
                    sourceRow.LocationId,
                    sourceRow.ItemId,
                    sourceRow.LotId,
                    sourceRow.SerialNumberId,
                    sourceRow.SerialNumber,
                    sourceRow.LicensePlateId,
                    sourceRow.InventoryStatusId,
                    sourceRow.BaseUnitOfMeasure,
                    sourceRow.OwnerKind,
                    sourceRow.InventoryOwnerId,
                    sourceRow.OwnerCodeSnapshot);
                var occurredAtUtc = DateTime.UtcNow.AddMinutes(-2);
                context.InventoryTransactions.AddRange(Enumerable.Range(1, 1_001).Select(index =>
                    new InventoryTransaction(
                        InventoryTransactionType.Adjustment,
                        key,
                        quantityDelta: 8m,
                        quantityBefore: 10m,
                        quantityAfter: 18m,
                        reservedQuantityDelta: 0m,
                        reservedQuantityBefore: 0m,
                        reservedQuantityAfter: 0m,
                        actorUserId: "postgres-anomaly-fixture",
                        occurredAtUtc,
                        correlationId: $"pg-anomaly-cap-{index}",
                        idempotencyKey: $"pg-anomaly-cap-{index}",
                        transactionGroupId: $"pg-anomaly-cap-{index}",
                        entrySequence: 1)));
                await context.SaveChangesAsync();

                var cappedToUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
                var cappedRun = await anomalies.RecalculateAsync(new AnomalyRecalculationInput(
                    scenario.WarehouseId,
                    cappedToUtc.AddDays(-7),
                    cappedToUtc));
                Assert.True(cappedRun.IsSuccess, cappedRun.Error);
                Assert.Contains(
                    "InventoryAdjustment-source-capped-at-1000-rule-skipped",
                    cappedRun.Value.DataQualityFlags);
            }

            using var forecastList = await client.GetAsync(
                $"/api/forecasting?warehouseId={scenario.WarehouseId}&page=1&pageSize=10");
            var forecastListBody = await forecastList.Content.ReadAsStringAsync();
            Assert.True(
                forecastList.StatusCode == HttpStatusCode.OK,
                $"PostgreSQL forecast list returned {(int)forecastList.StatusCode}: {forecastListBody}");
            using var forecastListSnapshot = JsonDocument.Parse(forecastListBody);
            var forecastRun = Assert.Single(forecastListSnapshot.RootElement
                .GetProperty("items").EnumerateArray());
            var forecastRunId = forecastRun.GetProperty("id").GetInt32();
            Assert.Equal("InsufficientData", forecastRun.GetProperty("dataStatus").GetString());

            using var forecastDetails = await client.GetAsync($"/api/forecasting/{forecastRunId}");
            Assert.Equal(HttpStatusCode.OK, forecastDetails.StatusCode);
            using var forecastDetailsSnapshot = JsonDocument.Parse(
                await forecastDetails.Content.ReadAsStringAsync());
            Assert.Equal(
                "Unknown",
                forecastDetailsSnapshot.RootElement.GetProperty("riskLevel").GetString());
            Assert.Empty(forecastDetailsSnapshot.RootElement.GetProperty("points").EnumerateArray());

            using var forecastExport = await client.GetAsync($"/api/forecasting/{forecastRunId}/export.csv");
            Assert.Equal(HttpStatusCode.OK, forecastExport.StatusCode);
            Assert.Equal("text/csv", forecastExport.Content.Headers.ContentType?.MediaType);

            using var page = await client.GetAsync("/Dashboard?culture=en-US&ui-culture=en-US");
            var pageHtml = await page.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("data-dashboard-value=\"inventory.onHandQuantity\">15.0</div>", pageHtml, StringComparison.Ordinal);
            Assert.Contains("America/Chicago", pageHtml, StringComparison.Ordinal);

            using var otherWarehouse = await client.GetAsync(
                $"/Dashboard/RefreshData?warehouseId={scenario.OtherWarehouseId}");
            Assert.Equal(HttpStatusCode.Forbidden, otherWarehouse.StatusCode);
            using var otherForecast = await client.GetAsync(
                $"/api/forecasting?warehouseId={scenario.OtherWarehouseId}");
            Assert.Equal(HttpStatusCode.Forbidden, otherForecast.StatusCode);
        }
        finally
        {
            await factory.DisposeDatabaseAsync();
            factory.Dispose();
        }
    }

    private sealed class PostgreSqlDashboardApplicationFactory(string baseConnectionString)
        : WebApplicationFactory<Wms.ASP.Program>
    {
        private readonly string _schema = "wms_dashboard_" + Guid.NewGuid().ToString("N");
        private readonly string _dataProtectionPath = Path.Combine(
            Path.GetTempPath(),
            "warecommand-dashboard-pg-" + Guid.NewGuid().ToString("N"),
            "keys");
        private string? _connectionString;

        public async Task CreateIsolatedDatabaseAsync()
        {
            var connectionBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                ApplicationName = "WareCommand.Dashboard.PostgreSqlTests",
                Pooling = false
            };
            await using (var connection = new NpgsqlConnection(connectionBuilder.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    $"CREATE SCHEMA \"{_schema}\"",
                    connection);
                await command.ExecuteNonQueryAsync();
            }

            connectionBuilder.SearchPath = _schema;
            _connectionString = connectionBuilder.ConnectionString;
            var options = new DbContextOptionsBuilder<WmsDbContext>()
                .UseNpgsql(_connectionString)
                .Options;
            await using var context = new WmsDbContext(options);
            await context.Database.MigrateAsync();
        }

        public async Task<WmsUser> CreateUserAsync()
        {
            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<WmsUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userName = "dashboard-pg-" + Guid.NewGuid().ToString("N")[..8];
            var user = new WmsUser
            {
                UserName = userName,
                Email = userName + "@example.test",
                EmailConfirmed = true,
                DisplayName = "Dashboard PostgreSQL Operator",
                EmployeeCode = "EMP-" + Guid.NewGuid().ToString("N")[..8],
                Locale = "en-US",
                TimeZone = "UTC",
                IsActive = true,
                LockoutEnabled = true
            };
            var created = await userManager.CreateAsync(user, "ValidPassword123!");
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));
            var role = await roleManager.FindByNameAsync(WmsRoleNames.WarehouseManager);
            Assert.NotNull(role);
            var assigned = await userManager.AddToRoleAsync(user, role.Name!);
            Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(error => error.Description)));
            return user;
        }

        public async Task<(string LocationCode, string ItemSku, int OtherWarehouseId, int WarehouseId, int ItemId)> CreateScenarioAsync(string userId)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            var token = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var warehouse = new Warehouse(
                "PGD-" + token,
                "Dashboard PostgreSQL Warehouse",
                timeZone: "America/Chicago");
            var otherWarehouse = new Warehouse("PGX-" + token, "Other PostgreSQL Warehouse");
            var item = new Item("PGI-" + token, "Dashboard PostgreSQL Item", "EA");
            context.AddRange(warehouse, otherWarehouse, item);
            await context.SaveChangesAsync();

            var location = new Location("PGL-" + token, "Dashboard PostgreSQL Location", warehouse.Id);
            var otherLocation = new Location("PGX-LOC-" + token, "Other PostgreSQL Location", otherWarehouse.Id);
            context.AddRange(location, otherLocation);
            await context.SaveChangesAsync();
            context.Stock.Add(new Stock(item.Id, location.Id, new Quantity(10)));
            context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
            {
                UserId = userId,
                WarehouseId = warehouse.Id,
                IsDefault = true
            });
            await context.SaveChangesAsync();
            return (location.Code, item.Sku, otherWarehouse.Id, warehouse.Id, item.Id);
        }

        public async Task DisposeDatabaseAsync()
        {
            if (_connectionString is null)
            {
                return;
            }

            var connectionBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                ApplicationName = "WareCommand.Dashboard.PostgreSqlTests.Cleanup",
                Pooling = false
            };
            await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
            var dataProtectionRoot = Directory.GetParent(_dataProtectionPath)?.FullName;
            if (dataProtectionRoot is not null && Directory.Exists(dataProtectionRoot))
            {
                Directory.Delete(dataProtectionRoot, recursive: true);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_dataProtectionPath);
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            builder.UseSetting("Wms:DatabaseProvider", "PostgreSql");
            builder.UseSetting("Wms:SeedProfile", "None");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _connectionString,
                    ["Wms:DatabaseProvider"] = "PostgreSql",
                    ["Wms:SeedProfile"] = "None",
                    ["Authentication:CookieSecure"] = "false",
                    ["Authentication:CookieHours"] = "8",
                    ["HttpsRedirection:Enabled"] = "false",
                    ["DataProtection:KeyDirectory"] = _dataProtectionPath,
                    ["AllowedHosts"] = "*"
                });
            });
        }
    }
}

public sealed class PostgreSqlDashboardFactAttribute : FactAttribute
{
    public PostgreSqlDashboardFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "WARECOMMAND_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Set WARECOMMAND_TEST_POSTGRES_CONNECTION or run scripts/verify-postgresql.ps1.";
        }
    }
}
