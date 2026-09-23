using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlDashboardFlowTests
{
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

            using var page = await client.GetAsync("/Dashboard?culture=en-US&ui-culture=en-US");
            var pageHtml = await page.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("data-dashboard-value=\"inventory.onHandQuantity\">15.0</div>", pageHtml, StringComparison.Ordinal);
            Assert.Contains("America/Chicago", pageHtml, StringComparison.Ordinal);

            using var otherWarehouse = await client.GetAsync(
                $"/Dashboard/RefreshData?warehouseId={scenario.OtherWarehouseId}");
            Assert.Equal(HttpStatusCode.Forbidden, otherWarehouse.StatusCode);
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

        public async Task<(string LocationCode, string ItemSku, int OtherWarehouseId)> CreateScenarioAsync(string userId)
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
            return (location.Code, item.Sku, otherWarehouse.Id);
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
