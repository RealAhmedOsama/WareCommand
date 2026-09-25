using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wms.Application.AnomalyDetection;
using Wms.Application.DataGeneration;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Performance;
using Wms.Application.Recommendations;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Tests.Integration;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlDashboardFlowTests
{
    private static readonly JsonSerializerOptions AssistantJsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

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

    [PostgreSqlDashboardFact]
    public async Task GeneratedDatasetDrivesAuthenticatedMvcAndBoundedInventoryReads()
    {
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var fixture = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.MinimalDevelopment,
                "issue-129-http-fixture",
                Environment: "Testing",
                Locale: "en-US"));
        Assert.True(fixture.Report.ReconciliationClean);
        var fixtureJson = JsonSerializer.Serialize(fixture);
        Assert.DoesNotContain("password", fixtureJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.ActorCredentials.Password, fixtureJson, StringComparison.Ordinal);

        var factory = new PostgreSqlDashboardApplicationFactory(
            Environment.GetEnvironmentVariable("WARECOMMAND_TEST_POSTGRES_CONNECTION")!);
        try
        {
            factory.UseExistingSchema(target.TestConnectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            using var login = await AuthenticationFlowTests.PostLoginAsync(
                client,
                fixture.ActorCredentials.UserName,
                fixture.ActorCredentials.Password);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

            using var dashboard = await client.GetAsync("/Dashboard?culture=en-US&ui-culture=en-US");
            var dashboardHtml = await dashboard.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
            Assert.Contains("lang=\"en-US\" dir=\"ltr\"", dashboardHtml, StringComparison.Ordinal);

            int warehouseId;
            string itemSku;
            string receivingLocationCode;
            using (var context = target.CreateContext())
            {
                warehouseId = await context.Warehouses
                    .OrderBy(value => value.Code)
                    .Select(value => value.Id)
                    .FirstAsync();
                itemSku = await context.Items.AsNoTracking()
                    .Where(value => !value.RequiresLot && !value.RequiresSerial)
                    .OrderBy(value => value.Sku)
                    .Select(value => value.Sku)
                    .FirstAsync();
                receivingLocationCode = await context.Locations.AsNoTracking()
                    .Where(value => value.WarehouseId == warehouseId && value.Type == LocationType.Receiving)
                    .Select(value => value.Code)
                    .FirstAsync();
            }

            using var inventoryPage = await client.GetAsync("/Inventory?showSummary=true");
            Assert.Equal(HttpStatusCode.OK, inventoryPage.StatusCode);
            using var dashboardData = await client.GetAsync($"/Dashboard/RefreshData?warehouseId={warehouseId}");
            Assert.Equal(HttpStatusCode.OK, dashboardData.StatusCode);

            var statuses = new System.Collections.Concurrent.ConcurrentBag<HttpStatusCode>();
            var dashboardLatencies = new System.Collections.Concurrent.ConcurrentBag<double>();
            var dashboardBatch = Stopwatch.StartNew();
            await Parallel.ForEachAsync(
                Enumerable.Range(0, 8),
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (_, cancellationToken) =>
                {
                    var requestWatch = Stopwatch.StartNew();
                    using var response = await client.GetAsync(
                        $"/Dashboard/RefreshData?warehouseId={warehouseId}",
                        cancellationToken);
                    await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    requestWatch.Stop();
                    statuses.Add(response.StatusCode);
                    dashboardLatencies.Add(requestWatch.Elapsed.TotalMilliseconds);
                });
            dashboardBatch.Stop();
            Assert.Equal(8, statuses.Count);
            Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
            var orderedDashboardLatencies = dashboardLatencies.Order().ToArray();
            var loginDashboardBudget = PerformanceWorkloadCatalog.Default.Single(
                value => value.Workload == PerformanceWorkloadKind.LoginDashboard);
            static double Percentile(double[] ordered, int percentile)
            {
                var index = Math.Clamp((int)Math.Ceiling(percentile / 100d * ordered.Length) - 1, 0, ordered.Length - 1);
                return Math.Round(ordered[index], 3);
            }
            var dashboardP50 = Percentile(orderedDashboardLatencies, 50);
            var dashboardP95 = Percentile(orderedDashboardLatencies, 95);
            var dashboardP99 = Percentile(orderedDashboardLatencies, 99);
            Console.WriteLine(
                $"postgres-performance-smoke workload=dashboard-refresh samples={orderedDashboardLatencies.Length} concurrency=4 throughputPerSecond={8d / dashboardBatch.Elapsed.TotalSeconds:F3} p50Milliseconds={dashboardP50:F3} p95Milliseconds={dashboardP95:F3} p99Milliseconds={dashboardP99:F3} status=200");
            Assert.True(
                (decimal)dashboardP99 <= loginDashboardBudget.MaximumP99Milliseconds,
                $"Authenticated dashboard refresh p99 {dashboardP99:F3} ms exceeded the existing {loginDashboardBudget.MaximumP99Milliseconds} ms budget.");

            using var receivePage = await client.GetAsync("/Receiving/Receive");
            var receiveHtml = await receivePage.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, receivePage.StatusCode);
            var tokenMatch = Regex.Match(
                receiveHtml,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.True(tokenMatch.Success, "Could not find the generated-actor receiving antiforgery token.");

            using var receipt = await client.PostAsync(
                "/Receiving/Receive",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["ItemSku"] = itemSku,
                    ["LocationCode"] = receivingLocationCode,
                    ["Quantity"] = "1",
                    ["UnitOfMeasure"] = "EA",
                    ["ReferenceNumber"] = "GEN-HTTP-RECEIPT",
                    ["__RequestVerificationToken"] = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value)
                }));
            Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
            Assert.True(fixture.Report.ActualCounts["inventoryRows"] >= 25);
        }
        finally
        {
            await factory.DisposeDatabaseAsync();
            factory.Dispose();
        }
    }

    [PostgreSqlDashboardFact]
    public async Task AuthorizedRecommendationHttpLifecycleUsesPostgreSqlAndCreatesNormalWork()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            "WARECOMMAND_TEST_POSTGRES_CONNECTION")!;
        await using var target = new PostgreSqlTestDatabase();
        await target.InitializeAsync();
        var fixture = await DeterministicPostgreSqlDataGenerationFixture.WriteWithActorCredentialsAsync(
            target,
            new DataGenerationRequest(
                WmsDataGenerationProfiles.IntegrationTest,
                "issue-128-recommendation-http",
                Environment: "Testing",
                Locale: "en-US"));
        var factory = new PostgreSqlDashboardApplicationFactory(baseConnectionString)
        {
            RecommendationsEnabled = true
        };
        try
        {
            factory.UseExistingSchema(target.TestConnectionString);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            using var login = await AuthenticationFlowTests.PostLoginAsync(
                client,
                fixture.ActorCredentials.UserName,
                fixture.ActorCredentials.Password);
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

            int warehouseId;
            int itemId;
            int destinationLocationId;
            string baseUnitOfMeasure;
            InventoryReplenishmentPolicyInput policyInput;
            using (var scope = factory.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var context = services.GetRequiredService<WmsDbContext>();
                warehouseId = await context.UserWarehouseAssignments.AsNoTracking()
                    .Where(value => value.UserId == fixture.ActorCredentials.UserId)
                    .Select(value => value.WarehouseId)
                    .OrderBy(value => value)
                    .FirstAsync();
                var source = await context.InventoryBalances.AsNoTracking()
                    .Include(value => value.Item)
                    .Include(value => value.Location)
                    .Where(value => value.WarehouseId == warehouseId &&
                                    value.Location.Type == LocationType.Storage &&
                                    value.Location.IsActive &&
                                    value.Location.IsPickable &&
                                    value.Item.IsActive &&
                                    !value.Item.RequiresLot &&
                                    !value.Item.RequiresSerial &&
                                    value.LotId == null &&
                                    value.SerialNumberId == null &&
                                    value.LicensePlateId == null &&
                                    value.InventoryStatusId == InventoryStatusSystemIds.Available &&
                                    value.OwnerKind == InventoryOwnerKind.CompanyOwned &&
                                    value.InventoryOwnerId == null &&
                                    value.OwnerCodeSnapshot == InventoryOwnershipDimension.CompanyOwnerCode)
                    .OrderBy(value => value.ItemId)
                    .ThenBy(value => value.LocationId)
                    .FirstAsync(value => value.OnHandQuantity - value.ReservedQuantity >= 1m);
                var destination = await context.Locations.AsNoTracking()
                    .Where(value => value.WarehouseId == warehouseId &&
                                    value.Type == LocationType.Storage &&
                                    value.Id != source.LocationId &&
                                    value.IsActive &&
                                    value.IsPickable)
                    .OrderBy(value => value.Id)
                    .FirstAsync();
                var destinationQuantity = await context.InventoryBalances.AsNoTracking()
                    .Where(value => value.LocationId == destination.Id && value.ItemId == source.ItemId)
                    .Select(value => (decimal?)value.OnHandQuantity)
                    .SumAsync() ?? 0m;
                var targetQuantity = destinationQuantity + 1m;
                policyInput = new InventoryReplenishmentPolicyInput(
                    source.ItemId,
                    warehouseId,
                    destination.Id,
                    MinimumQuantity: 0m,
                    MaximumQuantity: targetQuantity + 100m,
                    SafetyStockQuantity: 0m,
                    ReorderPointQuantity: targetQuantity,
                    TargetQuantity: targetQuantity,
                    QuantityBasis: InventoryPolicyQuantityBasis.PhysicalAvailable,
                    EffectiveFromUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                itemId = source.ItemId;
                destinationLocationId = destination.Id;
                baseUnitOfMeasure = source.BaseUnitOfMeasure;
            }

            using var itemsPage = await client.GetAsync("/Items");
            var itemsHtml = await itemsPage.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, itemsPage.StatusCode);
            var tokenMatch = Regex.Match(
                itemsHtml,
                "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Assert.True(tokenMatch.Success, "Could not read the recommendation API antiforgery token.");
            var antiforgeryToken = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value);

            using var policyResponse = await PostRecommendationAsync(
                client,
                "/api/inventory/replenishment-policies",
                policyInput,
                antiforgeryToken);
            var policyBody = await policyResponse.Content.ReadAsStringAsync();
            Assert.True(policyResponse.StatusCode == HttpStatusCode.OK,
                $"Replenishment policy creation returned {(int)policyResponse.StatusCode}: {policyBody}");
            var savedPolicy = JsonSerializer.Deserialize<InventoryReplenishmentPolicyDto>(policyBody, AssistantJsonOptions);
            Assert.NotNull(savedPolicy);
            var policyId = savedPolicy.Id;

            using var generate = await PostRecommendationAsync(
                client,
                "/api/recommendations/replenishment/generate",
                new { warehouseId, limit = 10 },
                antiforgeryToken);
            var generateBody = await generate.Content.ReadAsStringAsync();
            Assert.True(generate.StatusCode == HttpStatusCode.OK,
                $"Recommendation generation returned {(int)generate.StatusCode}: {generateBody}");
            var generated = JsonSerializer.Deserialize<RecommendationRecord[]>(generateBody, AssistantJsonOptions);
            var proposal = Assert.Single(generated!);
            Assert.Equal(RecommendationStatus.Proposed, proposal.Status);
            Assert.Equal(policyId.ToString(CultureInfo.InvariantCulture), proposal.Action.TargetReference);

            using var list = await client.GetAsync(
                $"/api/recommendations?warehouseId={warehouseId}&type=Replenishment&status=Proposed");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var listed = await list.Content.ReadFromJsonAsync<RecommendationPage>(AssistantJsonOptions);
            Assert.NotNull(listed);
            Assert.Contains(listed.Items, value => value.RecommendationId == proposal.RecommendationId);

            using var review = await PostRecommendationAsync(
                client,
                $"/api/recommendations/{proposal.RecommendationId}/review",
                new
                {
                    expectedRevision = proposal.Revision,
                    idempotencyKey = "issue-128-http-review",
                    comment = "Reviewed against the current deterministic replenishment policy."
                },
                antiforgeryToken);
            var reviewBody = await review.Content.ReadAsStringAsync();
            Assert.True(review.StatusCode == HttpStatusCode.OK,
                $"Recommendation review returned {(int)review.StatusCode}: {reviewBody}");
            var reviewed = await review.Content.ReadFromJsonAsync<RecommendationRecord>(AssistantJsonOptions);
            Assert.NotNull(reviewed);
            Assert.Equal(RecommendationStatus.Reviewed, reviewed.Status);

            using var approve = await PostRecommendationAsync(
                client,
                $"/api/recommendations/{proposal.RecommendationId}/approve",
                new { expectedRevision = reviewed.Revision, idempotencyKey = "issue-128-http-approve" },
                antiforgeryToken);
            var approveBody = await approve.Content.ReadAsStringAsync();
            Assert.True(approve.StatusCode == HttpStatusCode.OK,
                $"Recommendation approval returned {(int)approve.StatusCode}: {approveBody}");
            var approved = await approve.Content.ReadFromJsonAsync<RecommendationRecord>(AssistantJsonOptions);
            Assert.NotNull(approved);
            Assert.Equal(RecommendationStatus.Approved, approved.Status);

            using var execute = await PostRecommendationAsync(
                client,
                $"/api/recommendations/{proposal.RecommendationId}/execute",
                new { expectedRevision = approved.Revision, idempotencyKey = "issue-128-http-execute" },
                antiforgeryToken);
            var executeBody = await execute.Content.ReadAsStringAsync();
            Assert.True(execute.StatusCode == HttpStatusCode.OK,
                $"Recommendation execution returned {(int)execute.StatusCode}: {executeBody}");
            var executed = JsonSerializer.Deserialize<RecommendationRecord>(executeBody, AssistantJsonOptions);
            Assert.NotNull(executed);
            Assert.Equal(RecommendationStatus.Executed, executed.Status);
            Assert.StartsWith("warehouse-work:", executed.ExecutionReference, StringComparison.Ordinal);

            using var historyResponse = await client.GetAsync(
                $"/api/recommendations/{proposal.RecommendationId}/history");
            Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
            var history = await historyResponse.Content.ReadFromJsonAsync<RecommendationHistoryEntry[]>(AssistantJsonOptions);
            Assert.NotNull(history);
            Assert.Equal(["created", "reviewed", "approved", "executed"],
                history.Select(value => value.EventType));

            using var qualityResponse = await client.GetAsync(
                $"/api/recommendations/quality?warehouseId={warehouseId}");
            Assert.Equal(HttpStatusCode.OK, qualityResponse.StatusCode);
            var quality = await qualityResponse.Content.ReadFromJsonAsync<RecommendationQualityReport>(AssistantJsonOptions);
            Assert.NotNull(quality);
            Assert.False(quality.IsTruncated);
            var replenishmentQuality = Assert.Single(quality.Types, value =>
                value.Type == RecommendationType.Replenishment);
            Assert.Equal(1, replenishmentQuality.Generated);
            Assert.Equal(1, replenishmentQuality.BaselineMatched);
            Assert.Equal(1, replenishmentQuality.Executed);
            Assert.Contains("work-created", replenishmentQuality.ExecutionOutcomes.Keys);

            using var workerProfileRequest = new HttpRequestMessage(
                HttpMethod.Put,
                $"/api/workforce/profiles/{Uri.EscapeDataString(fixture.ActorCredentials.UserId)}")
            {
                Content = JsonContent.Create(new { warehouseId, timeZoneId = "UTC", isActive = true })
            };
            workerProfileRequest.Headers.TryAddWithoutValidation(
                "RequestVerificationToken",
                antiforgeryToken);
            using var workerProfileResponse = await client.SendAsync(workerProfileRequest);
            var workerProfileBody = await workerProfileResponse.Content.ReadAsStringAsync();
            Assert.True(workerProfileResponse.StatusCode == HttpStatusCode.OK,
                $"Worker profile setup returned {(int)workerProfileResponse.StatusCode}: {workerProfileBody}");

            using var workCreate = await PostRecommendationAsync(
                client,
                "/api/work",
                new
                {
                    creationKey = "issue-128-workload-self-claim",
                    type = "Other",
                    warehouseId,
                    sourceEntityType = "Issue128Integration",
                    sourceEntityId = "workload-priority",
                    priority = 30,
                    makeAvailable = true,
                    lines = new[]
                    {
                        new
                        {
                            sequence = 1,
                            warehouseId,
                            itemId,
                            plannedQuantity = 1m,
                            baseUnitOfMeasure
                        }
                    }
                },
                antiforgeryToken);
            var workCreateBody = await workCreate.Content.ReadAsStringAsync();
            Assert.True(workCreate.StatusCode == HttpStatusCode.OK,
                $"Self-claimable work creation returned {(int)workCreate.StatusCode}: {workCreateBody}");

            using var workloadGenerate = await PostRecommendationAsync(
                client,
                "/api/recommendations/generate/WorkloadPriority",
                new { warehouseId, limit = 50 },
                antiforgeryToken);
            var workloadGenerateBody = await workloadGenerate.Content.ReadAsStringAsync();
            Assert.True(workloadGenerate.StatusCode == HttpStatusCode.OK,
                $"Workload recommendation generation returned {(int)workloadGenerate.StatusCode}: {workloadGenerateBody}");
            var workloadProposals = JsonSerializer.Deserialize<RecommendationRecord[]>(
                workloadGenerateBody,
                AssistantJsonOptions);
            Assert.NotEmpty(workloadProposals!);
            var workloadProposal = workloadProposals!.First(value =>
                value.Action.ActionType == "work-claim");
            var workloadWorkId = int.Parse(
                workloadProposal.Action.TargetReference,
                CultureInfo.InvariantCulture);

            using var workloadReview = await PostRecommendationAsync(
                client,
                $"/api/recommendations/{workloadProposal.RecommendationId}/review",
                new { expectedRevision = workloadProposal.Revision, idempotencyKey = "issue-128-workload-review", comment = "Reviewed the deterministic queue ranking." },
                antiforgeryToken);
            Assert.Equal(HttpStatusCode.OK, workloadReview.StatusCode);
            var workloadReviewed = await workloadReview.Content.ReadFromJsonAsync<RecommendationRecord>(AssistantJsonOptions);
            Assert.NotNull(workloadReviewed);
            using var workloadApprove = await PostRecommendationAsync(
                client,
                $"/api/recommendations/{workloadProposal.RecommendationId}/approve",
                new { expectedRevision = workloadReviewed.Revision, idempotencyKey = "issue-128-workload-approve" },
                antiforgeryToken);
            Assert.Equal(HttpStatusCode.OK, workloadApprove.StatusCode);
            var workloadApproved = await workloadApprove.Content.ReadFromJsonAsync<RecommendationRecord>(AssistantJsonOptions);
            Assert.NotNull(workloadApproved);
            using var workloadExecute = await PostRecommendationAsync(
                client,
                $"/api/recommendations/{workloadProposal.RecommendationId}/execute",
                new { expectedRevision = workloadApproved.Revision, idempotencyKey = "issue-128-workload-execute" },
                antiforgeryToken);
            var workloadExecuteBody = await workloadExecute.Content.ReadAsStringAsync();
            Assert.True(workloadExecute.StatusCode == HttpStatusCode.OK,
                $"Workload recommendation execution returned {(int)workloadExecute.StatusCode}: {workloadExecuteBody}");
            var workloadExecuted = JsonSerializer.Deserialize<RecommendationRecord>(
                workloadExecuteBody,
                AssistantJsonOptions);
            Assert.NotNull(workloadExecuted);
            Assert.Equal(RecommendationStatus.Executed, workloadExecuted.Status);
            Assert.Equal($"warehouse-work:{workloadWorkId}", workloadExecuted.ExecutionReference);

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Single(await context.WarehouseWorks.AsNoTracking()
                    .Where(value => value.SourceEntityType == "InventoryReplenishmentPolicy" &&
                                    value.SourceEntityId == policyId.ToString(CultureInfo.InvariantCulture) &&
                                    value.WarehouseId == warehouseId)
                    .ToArrayAsync());
                var claimedWork = await context.WarehouseWorks.AsNoTracking()
                    .SingleAsync(value => value.Id == workloadWorkId);
                Assert.Equal(WarehouseWorkStatus.Assigned, claimedWork.Status);
                Assert.Equal(fixture.ActorCredentials.UserId, claimedWork.AssignedUserId);
                Assert.Equal(0m, await context.InventoryBalances.AsNoTracking()
                    .Where(value => value.LocationId == destinationLocationId && value.ItemId == itemId)
                    .Select(value => (decimal?)value.ReservedQuantity)
                    .SumAsync() ?? 0m);
            }
        }
        finally
        {
            await factory.DisposeDatabaseAsync();
            factory.Dispose();
        }
    }

    private static async Task<HttpResponseMessage> PostRecommendationAsync(
        HttpClient client,
        string path,
        object payload,
        string antiforgeryToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation("RequestVerificationToken", antiforgeryToken);
        return await client.SendAsync(request);
    }

    internal sealed class PostgreSqlDashboardApplicationFactory(string baseConnectionString)
        : WebApplicationFactory<Wms.ASP.Program>
    {
        private readonly string _schema = "wms_dashboard_" + Guid.NewGuid().ToString("N");
        private readonly string _dataProtectionPath = Path.Combine(
            Path.GetTempPath(),
            "warecommand-dashboard-pg-" + Guid.NewGuid().ToString("N"),
            "keys");
        private string? _connectionString;
        private bool _ownsSchema;
        public bool RecommendationsEnabled { get; set; }
        public int? RateLimitPermitLimitOverride { get; set; }

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
            _ownsSchema = true;
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

            if (_ownsSchema)
            {
                var connectionBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
                {
                    ApplicationName = "WareCommand.Dashboard.PostgreSqlTests.Cleanup",
                    Pooling = false
                };
                await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
                await command.ExecuteNonQueryAsync();
            }

            var dataProtectionRoot = Directory.GetParent(_dataProtectionPath)?.FullName;
            if (dataProtectionRoot is not null && Directory.Exists(dataProtectionRoot))
            {
                Directory.Delete(dataProtectionRoot, recursive: true);
            }
        }

        public void UseExistingSchema(
            string scopedConnectionString,
            string applicationName = "WareCommand.GeneratedDataset.BrowserTests",
            bool pooling = false,
            int maximumPoolSize = 100)
        {
            if (_connectionString is not null)
            {
                throw new InvalidOperationException("The dashboard test database was already configured.");
            }

            if (maximumPoolSize is < 1 or > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPoolSize));
            }

            var connectionBuilder = new NpgsqlConnectionStringBuilder(scopedConnectionString)
            {
                ApplicationName = applicationName,
                Pooling = pooling,
                MaxPoolSize = maximumPoolSize
            };
            if (string.IsNullOrWhiteSpace(connectionBuilder.SearchPath) ||
                !connectionBuilder.SearchPath.StartsWith("wms_test_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The generated dataset browser fixture requires an isolated test schema.");
            }

            _connectionString = connectionBuilder.ConnectionString;
            _ownsSchema = false;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_dataProtectionPath);
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
            builder.UseSetting("Wms:DatabaseProvider", "PostgreSql");
            builder.UseSetting("Wms:SeedProfile", "None");
            if (RateLimitPermitLimitOverride is int configuredPermitLimit)
            {
                var configuredValue = configuredPermitLimit.ToString(CultureInfo.InvariantCulture);
                builder.UseSetting("Security:RateLimiting:GlobalPermitLimit", configuredValue);
                builder.UseSetting("Security:RateLimiting:ReportPermitLimit", configuredValue);
                builder.UseSetting("Security:RateLimiting:ApiPermitLimit", configuredValue);
                builder.UseSetting("Security:RateLimiting:ScanningPermitLimit", configuredValue);
                builder.UseSetting("Security:RateLimiting:ImportPermitLimit", configuredValue);
            }
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _connectionString,
                    ["Wms:DatabaseProvider"] = "PostgreSql",
                    ["Wms:SeedProfile"] = "None",
                    ["Authentication:CookieSecure"] = "false",
                    ["Authentication:CookieHours"] = "8",
                    ["HttpsRedirection:Enabled"] = "false",
                    ["DataProtection:KeyDirectory"] = _dataProtectionPath,
                    ["Recommendations:Enabled"] = RecommendationsEnabled.ToString(),
                    ["Recommendations:KillSwitchEnabled"] = "false",
                    ["Recommendations:ProviderAvailable"] = "true",
                    ["Recommendations:ShadowMode"] = "true",
                    ["Recommendations:ProviderName"] = "deterministic-wms",
                    ["AllowedHosts"] = "*"
                };
                if (RateLimitPermitLimitOverride is int permitLimit)
                {
                    var value = permitLimit.ToString(CultureInfo.InvariantCulture);
                    settings["Security:RateLimiting:GlobalPermitLimit"] = value;
                    settings["Security:RateLimiting:ReportPermitLimit"] = value;
                    settings["Security:RateLimiting:ApiPermitLimit"] = value;
                    settings["Security:RateLimiting:ScanningPermitLimit"] = value;
                    settings["Security:RateLimiting:ImportPermitLimit"] = value;
                }

                configuration.AddInMemoryCollection(settings);
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
