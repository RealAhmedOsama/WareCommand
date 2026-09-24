using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Tests.Integration;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class PostgreSqlBrowserJourneyTests
{
    private const string TestPassword = "ValidPassword123!";
    private static readonly JsonSerializerOptions FailureEvidenceJsonOptions = new()
    {
        WriteIndented = true
    };

    [PostgreSqlBrowserFact]
    public async Task Real_browser_qualifies_authenticated_workflows_locales_and_offline_scan_replay()
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(
            "WARECOMMAND_TEST_POSTGRES_CONNECTION")!;
        await using var database = new PostgreSqlTestDatabase();
        await database.InitializeAsync();

        var browserConnectionBuilder = new NpgsqlConnectionStringBuilder(database.TestConnectionString)
        {
            ApplicationName = "WareCommand.PostgreSqlBrowserJourney",
            Pooling = true,
            MaxPoolSize = 32
        };
        var browserConnectionString = browserConnectionBuilder.ConnectionString;
        var factory = new PostgreSqlDashboardFlowTests.PostgreSqlDashboardApplicationFactory(
            baseConnectionString);
        factory.UseExistingSchema(
            browserConnectionString,
            pooling: true,
            maximumPoolSize: 32);
        BrowserTestHost? host = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IPage? page = null;
        var events = new ConcurrentQueue<BrowserEvidenceEvent>();
        var expectedNetworkConsoleErrors = new ConcurrentQueue<string>();
        var expectedOfflineFailure = 0;
        TaskCompletionSource<bool>? expectedOfflineScanRequest = null;
        var expectedReceivingApiRejection = 0;
        var expectedResponseLoss = 0;
        var simulateResponseLoss = 1;
        string? activeCase = null;

        try
        {
            var firstActor = await factory.CreateUserAsync();
            var firstScenario = await factory.CreateScenarioAsync(firstActor.Id);
            var secondActor = await factory.CreateUserAsync();
            var secondScenario = await factory.CreateScenarioAsync(secondActor.Id);
            var firstLocationId = 0;
            var secondLocationId = 0;
            var destinationLocationCode = "";
            var switchWarehouseId = 0;

            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
                firstLocationId = await context.Locations.AsNoTracking()
                    .Where(location => location.Code == firstScenario.LocationCode)
                    .Select(location => location.Id)
                    .SingleAsync();
                secondLocationId = await context.Locations.AsNoTracking()
                    .Where(location => location.Code == secondScenario.LocationCode)
                    .Select(location => location.Id)
                    .SingleAsync();
                var firstItem = await context.Items.SingleAsync(item => item.Id == firstScenario.ItemId);
                firstItem.AddBarcode(new Barcode(firstScenario.ItemSku));

                var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                destinationLocationCode = "BRW-DST-" + suffix;
                context.Locations.Add(new Wms.Domain.Entities.Location(
                    destinationLocationCode,
                    "Browser qualification destination",
                    firstScenario.WarehouseId));
                var switchWarehouse = new Warehouse(
                    "BRW-SW-" + suffix,
                    "Browser qualification switch warehouse");
                context.Warehouses.Add(switchWarehouse);
                await context.SaveChangesAsync();
                switchWarehouseId = switchWarehouse.Id;
                context.UserWarehouseAssignments.Add(new WmsUserWarehouseAssignment
                {
                    UserId = firstActor.Id,
                    WarehouseId = switchWarehouseId,
                    IsDefault = false
                });
                await context.SaveChangesAsync();
            }

            var destinationLocationId = 0;
            using (var scope = factory.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
                destinationLocationId = await context.Locations.AsNoTracking()
                    .Where(location => location.Code == destinationLocationCode)
                    .Select(location => location.Id)
                    .SingleAsync();
            }

            host = await BrowserTestHost.StartAsync(
                FindRepositoryRoot(),
                browserConnectionString);
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
            var contextOptions = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1366, Height = 768 },
                Locale = "en-US",
                ServiceWorkers = ServiceWorkerPolicy.Allow
            });
            page = await contextOptions.NewPageAsync();
            page.SetDefaultTimeout(20_000);

            page.Console += (_, message) =>
            {
                if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var caseId = activeCase ?? "browser";
                    var expected = Volatile.Read(ref expectedReceivingApiRejection) != 0 &&
                        message.Text.StartsWith("Failed to load resource", StringComparison.OrdinalIgnoreCase);
                    if (!expected && expectedNetworkConsoleErrors.TryPeek(out var expectedCaseId) &&
                        string.Equals(expectedCaseId, caseId, StringComparison.Ordinal))
                    {
                        expected = expectedNetworkConsoleErrors.TryDequeue(out var matchedCaseId) &&
                                   string.Equals(matchedCaseId, caseId, StringComparison.Ordinal);
                    }

                    events.Enqueue(new BrowserEvidenceEvent(
                        DateTimeOffset.UtcNow,
                        caseId,
                        "console-error",
                        string.Empty,
                        null,
                        expected));
                }
            };
            page.PageError += (_, _) => events.Enqueue(new BrowserEvidenceEvent(
                DateTimeOffset.UtcNow,
                activeCase ?? "browser",
                "javascript-error",
                string.Empty,
                null,
                false));
            page.RequestFailed += (_, request) =>
            {
                var caseId = activeCase ?? "browser";
                var safePath = SafePath(request.Url);
                var expected = Volatile.Read(ref expectedOfflineFailure) != 0 &&
                    IsExpectedOfflineFailure(caseId, safePath);
                if (!expected &&
                    string.Equals(caseId, "response-loss-retry-is-idempotent", StringComparison.Ordinal) &&
                    IsReceivingScanPath(request.Url) &&
                    Interlocked.Exchange(ref expectedResponseLoss, 0) != 0)
                {
                    expected = true;
                }

                if (expected)
                {
                    expectedNetworkConsoleErrors.Enqueue(caseId);
                }

                events.Enqueue(new BrowserEvidenceEvent(
                    DateTimeOffset.UtcNow,
                    caseId,
                    "request-failed",
                    SafePath(request.Url),
                    null,
                    expected));
            };
            page.Response += (_, response) =>
            {
                var caseId = activeCase ?? "browser";
                var expected = Volatile.Read(ref expectedReceivingApiRejection) != 0 &&
                    ((string.Equals(caseId, "browser-accessible-scan-focus", StringComparison.Ordinal) &&
                      string.Equals(SafePath(response.Url), "/api/receiving/sessions", StringComparison.OrdinalIgnoreCase) &&
                      response.Status == (int)HttpStatusCode.BadRequest) ||
                     (string.Equals(caseId, "invalid-scanner-item-no-inventory-change", StringComparison.Ordinal) &&
                      IsReceivingScanPath(response.Url) &&
                      response.Status == (int)HttpStatusCode.NotFound));
                if (response.Status >= 400)
                {
                    events.Enqueue(new BrowserEvidenceEvent(
                        DateTimeOffset.UtcNow,
                        caseId,
                        "http-error",
                        SafePath(response.Url),
                        response.Status,
                        expected));
                }
            };
            page.Request += (_, request) =>
            {
                if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                    IsReceivingScanPath(request.Url))
                {
                    var caseId = activeCase ?? "browser";
                    if (Volatile.Read(ref expectedOfflineFailure) != 0)
                    {
                        expectedOfflineScanRequest?.TrySetResult(true);
                    }

                    events.Enqueue(new BrowserEvidenceEvent(
                        DateTimeOffset.UtcNow,
                        caseId,
                        "receiving-scan-request",
                        SafePath(request.Url),
                        null,
                        caseId is "offline-scan-queued-not-accepted" or
                            "response-loss-retry-is-idempotent" or
                            "invalid-scanner-item-no-inventory-change" or
                            "actor-session-isolation-after-logout"));
                }
            };

            activeCase = "anonymous-permission-boundary";
            var anonymousResponse = await page.GotoAsync(host.Origin + "/Inventory");
            Assert.NotNull(anonymousResponse);
            Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)anonymousResponse!.Status);
            Assert.Contains("/Account/Login", page.Url, StringComparison.Ordinal);

            activeCase = "actor-a-login";
            await LoginAsync(page, host.Origin, firstActor.UserName!);
            Assert.Equal("ltr", await page.Locator("html").GetAttributeAsync("dir"));

            activeCase = "warehouse-switch-and-authorization";
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Inventory", "en-US"));
            var warehouseSelector = page.Locator("#layout-warehouse");
            Assert.Equal(2, await warehouseSelector.Locator("option").CountAsync());
            await EnsureWarehouseSelectorVisibleAsync(page);
            var switchResponse = page.WaitForResponseAsync(response =>
                string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                SafePath(response.Url).EndsWith("/Warehouses/Select", StringComparison.Ordinal));
            await warehouseSelector.SelectOptionAsync(switchWarehouseId.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(HttpStatusCode.Found, (HttpStatusCode)(await switchResponse).Status);
            Assert.Equal(
                switchWarehouseId.ToString(CultureInfo.InvariantCulture),
                await page.Locator("#layout-warehouse").InputValueAsync());
            var returnResponse = page.WaitForResponseAsync(response =>
                string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                SafePath(response.Url).EndsWith("/Warehouses/Select", StringComparison.Ordinal));
            await EnsureWarehouseSelectorVisibleAsync(page);
            await page.Locator("#layout-warehouse").SelectOptionAsync(
                firstScenario.WarehouseId.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(HttpStatusCode.Found, (HttpStatusCode)(await returnResponse).Status);
            Assert.Equal(
                firstScenario.WarehouseId.ToString(CultureInfo.InvariantCulture),
                await page.Locator("#layout-warehouse").InputValueAsync());

            activeCase = "permission-restricted-settings";
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Settings", "en-US"));
            Assert.Contains("AccessDenied", page.Url, StringComparison.OrdinalIgnoreCase);
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Inventory", "en-US"));
            Assert.Equal(0, await page.Locator("a[href='/Settings']").CountAsync());

            activeCase = "existing-route-locale-viewport-matrix";
            var routes = new[]
            {
                "/Dashboard",
                "/Inventory?searchTerm=" + Uri.EscapeDataString(firstScenario.ItemSku),
                "/Receiving/Receive",
                "/Receiving/Putaway",
                "/Picking",
                "/Reports",
                "/Warehouses"
            };
            var viewports = new[]
            {
                (Width: 390, Height: 844),
                (Width: 768, Height: 1024),
                (Width: 1366, Height: 768)
            };
            foreach (var (culture, direction) in new[]
                     {
                         ("en-US", "ltr"),
                         ("ar-SA", "rtl")
                     })
            {
                foreach (var viewport in viewports)
                {
                    await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                    foreach (var route in routes)
                    {
                        activeCase = $"route-matrix-{culture}-{viewport.Width}-{SafeRouteName(route)}";
                        var response = await page.GotoAsync(LocalizedUrl(host.Origin, route, culture));
                        Assert.NotNull(response);
                        Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)response!.Status);
                        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
                        Assert.Equal(direction, await page.Locator("html").GetAttributeAsync("dir"));
                        Assert.Equal(1, await page.Locator("#main-content").CountAsync());
                        Assert.True(await HasNoPageOverflowAsync(page));
                    }
                }
            }

            activeCase = "browser-accessible-scan-focus";
            await page.SetViewportSizeAsync(390, 844);
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Receiving/Receive", "en-US"));
            Assert.NotEmpty(await page.Locator("label[for='receiving-session-scan']").InnerTextAsync());
            await page.Locator("#receiving-session-location").FillAsync(
                secondLocationId.ToString(CultureInfo.InvariantCulture));
            await page.Locator("#receiving-session-source").SelectOptionAsync("5");
            await page.Locator("#receiving-session-reason").FillAsync("Browser qualification override");
            var rejectedStartResponse = page.WaitForResponseAsync(response =>
                string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(SafePath(response.Url), "/api/receiving/sessions", StringComparison.OrdinalIgnoreCase) &&
                response.Status == (int)HttpStatusCode.BadRequest);
            Volatile.Write(ref expectedReceivingApiRejection, 1);
            await page.Locator("#receiving-session-start").ClickAsync();
            Assert.Equal((int)HttpStatusCode.BadRequest, (await rejectedStartResponse).Status);
            await page.Locator("#receiving-session-start-message").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            Volatile.Write(ref expectedReceivingApiRejection, 0);
            Assert.True(await page.Locator("#receiving-session-active").EvaluateAsync<bool>(
                "element => element.classList.contains('d-none')"));

            await page.Locator("#receiving-session-location").FillAsync(
                firstLocationId.ToString(CultureInfo.InvariantCulture));
            await page.Locator("#receiving-session-start").ClickAsync();
            await page.Locator("#receiving-session-active").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            Assert.Equal(
                "receiving-session-scan",
                await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''"));

            await page.EvaluateAsync("async () => { await navigator.serviceWorker.ready; }");
            await page.ReloadAsync();
            await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");
            var cacheIsSafe = await page.EvaluateAsync<bool>("""
                async () => {
                  const names = await caches.keys();
                  const paths = [];
                  for (const name of names) {
                    const cache = await caches.open(name);
                    for (const request of await cache.keys()) paths.push(new URL(request.url).pathname);
                  }
                  return names.some(name => name.startsWith('warecommand-')) &&
                    paths.every(path => !path.startsWith('/Dashboard') &&
                      !path.startsWith('/Inventory') && !path.startsWith('/Receiving') &&
                      !path.startsWith('/api/'));
                }
                """);
            Assert.True(cacheIsSafe);

            var sessionId = 0;
            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                var initialBalance = await ReadOnHandAsync(
                    context,
                    firstScenario.ItemId,
                    firstLocationId);
                Assert.Equal(10m, initialBalance);
                var receivingSession = await context.ReceivingSessions.AsNoTracking()
                    .SingleAsync(session => session.WarehouseId == firstScenario.WarehouseId);
                sessionId = receivingSession.Id;
            }

            var firstActorStorageKey = await page.Locator("body").GetAttributeAsync("data-wms-actor-key");
            Assert.False(string.IsNullOrWhiteSpace(firstActorStorageKey));

            activeCase = "offline-scan-queued-not-accepted";
            await page.Context.SetOfflineAsync(true);
            expectedOfflineScanRequest = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref expectedOfflineFailure, 1);
            var scanInput = page.Locator("#receiving-session-scan");
            Assert.True(await scanInput.IsEnabledAsync());
            await scanInput.FocusAsync();
            await page.Keyboard.TypeAsync(firstScenario.ItemSku);
            Assert.True(
                string.Equals(
                    await scanInput.InputValueAsync(),
                    firstScenario.ItemSku,
                    StringComparison.Ordinal),
                "The keyboard scan did not populate the receiving input.");
            await scanInput.PressAsync("Enter");
            await expectedOfflineScanRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await page.Locator("#receiving-session-queue-status").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-queue-status')?.textContent.trim().endsWith('1') === true");
            Assert.True(await page.Locator("#receiving-session-message").EvaluateAsync<bool>(
                "element => element.classList.contains('alert-warning')"));
            Volatile.Write(ref expectedOfflineFailure, 0);

            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Equal(10m, await ReadOnHandAsync(context, firstScenario.ItemId, firstLocationId));
                Assert.Equal(0, await context.ReceivingSessionScans.CountAsync());
            }

            activeCase = "response-loss-retry-is-idempotent";
            await page.Context.SetOfflineAsync(false);
            Assert.True(await page.EvaluateAsync<bool>("() => navigator.onLine"));
            Assert.Equal(
                (int)HttpStatusCode.OK,
                await page.EvaluateAsync<int>("async () => (await fetch('/health/live', { cache: 'no-store' })).status"));
            var firstResponseStatus = new TaskCompletionSource<(int Status, string ErrorCode, string Method, string Path)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync(
                "**/api/receiving/sessions/*/scans",
                async route =>
                {
                    if (Interlocked.Exchange(ref simulateResponseLoss, 0) == 1)
                    {
                        try
                        {
                            var serverResponse = await route.FetchAsync(new RouteFetchOptions
                            {
                                Timeout = 15_000
                            });
                            var errorCode = string.Empty;
                            if (serverResponse.Status >= 400)
                            {
                                try
                                {
                                    using var errorDocument = JsonDocument.Parse(await serverResponse.TextAsync());
                                    if (errorDocument.RootElement.TryGetProperty("errorCode", out var errorCodeValue))
                                    {
                                        errorCode = errorCodeValue.GetString() ?? string.Empty;
                                    }
                                }
                                catch (JsonException)
                                {
                                    // Keep the test evidence to a stable code and normalized route only.
                                }
                            }

                            firstResponseStatus.TrySetResult((
                                serverResponse.Status,
                                errorCode,
                                route.Request.Method,
                                SafePath(route.Request.Url)));
                            if (serverResponse.Status != (int)HttpStatusCode.OK)
                            {
                                await route.AbortAsync();
                                return;
                            }

                            Volatile.Write(ref expectedResponseLoss, 1);
                            await route.AbortAsync();
                            return;
                        }
                        catch (Exception exception)
                        {
                            firstResponseStatus.TrySetException(new InvalidOperationException(
                                $"The intercepted receiving scan fetch failed ({exception.GetType().Name})."));
                            throw;
                        }
                    }

                    await route.ContinueAsync();
                });
            await page.Locator("#receiving-session-retry").ClickAsync();
            Assert.True(await page.Locator("#receiving-session-retry").IsDisabledAsync());
            var firstResponse = await firstResponseStatus.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(
                firstResponse.Status == (int)HttpStatusCode.OK,
                $"The intercepted {firstResponse.Method} {firstResponse.Path} request returned HTTP {firstResponse.Status} ({firstResponse.ErrorCode}).");
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-retry')?.disabled === false");
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-queue-status')?.textContent.trim().endsWith('1') === true");

            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Equal(11m, await ReadOnHandAsync(context, firstScenario.ItemId, firstLocationId));
                Assert.Equal(1, await context.ReceivingSessionScans.CountAsync(scan =>
                    scan.ReceivingSessionId == sessionId && scan.Status == ReceivingScanStatus.Completed));
            }

            await page.Locator("#receiving-session-retry").ClickAsync();
            Assert.True(await page.Locator("#receiving-session-retry").IsDisabledAsync());
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-queue-status')?.textContent.trim() === ''");
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-retry')?.disabled === false");
            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Equal(11m, await ReadOnHandAsync(context, firstScenario.ItemId, firstLocationId));
                Assert.Equal(1, await context.ReceivingSessionScans.CountAsync(scan =>
                    scan.ReceivingSessionId == sessionId && scan.Status == ReceivingScanStatus.Completed));
            }
            Interlocked.Exchange(ref expectedResponseLoss, 0);
            await page.UnrouteAsync("**/api/receiving/sessions/*/scans");

            activeCase = "invalid-scanner-item-no-inventory-change";
            var rejectedScanResponse = page.WaitForResponseAsync(response =>
                string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                IsReceivingScanPath(response.Url) &&
                response.Status == (int)HttpStatusCode.NotFound);
            Volatile.Write(ref expectedReceivingApiRejection, 1);
            await page.Locator("#receiving-session-scan").FocusAsync();
            await page.Keyboard.TypeAsync("NO-SUCH-ITEM");
            await page.Keyboard.PressAsync("Enter");
            Assert.Equal((int)HttpStatusCode.NotFound, (await rejectedScanResponse).Status);
            await page.Locator("#receiving-session-message.alert-danger").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            Volatile.Write(ref expectedReceivingApiRejection, 0);
            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Equal(11m, await ReadOnHandAsync(context, firstScenario.ItemId, firstLocationId));
                Assert.Equal(1, await context.ReceivingSessionScans.CountAsync(scan =>
                    scan.ReceivingSessionId == sessionId && scan.Status == ReceivingScanStatus.Completed));
            }

            activeCase = "putaway-durable-server-outcome";
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Receiving/Putaway", "en-US"));
            await page.Locator("#ItemSku").FillAsync(firstScenario.ItemSku);
            await page.Locator("#FromLocationCode").FillAsync(firstScenario.LocationCode);
            await page.Locator("#ToLocationCode").FillAsync(destinationLocationCode);
            await page.Locator("#Quantity").FillAsync("1");
            await page.Locator("form[action='/Receiving/Putaway'] button[type='submit']").ClickAsync();
            await page.Locator(".alert-success").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Equal(10m, await ReadOnHandAsync(context, firstScenario.ItemId, firstLocationId));
                Assert.Equal(1m, await ReadOnHandAsync(context, firstScenario.ItemId, destinationLocationId));
            }

            activeCase = "unsupported-offline-form-is-not-claimed-successful";
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Picking", "en-US"));
            await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");
            await page.Context.SetOfflineAsync(true);
            Assert.False(await page.EvaluateAsync<bool>("() => navigator.onLine"));
            Volatile.Write(ref expectedOfflineFailure, 1);
            await page.Locator("#OrderNumber").FillAsync("BROWSER-NO-ORDER");
            await page.Locator("#ItemSku").FillAsync(firstScenario.ItemSku);
            await page.Locator("#LocationCode").FillAsync(firstScenario.LocationCode);
            await page.Locator("#Quantity").FillAsync("1");
            var pickingForm = page.Locator("form[action='/Picking/Pick']");
            Assert.True(
                await pickingForm.EvaluateAsync<bool>("form => form.checkValidity()"),
                "The offline picking form should be valid before its submit is intercepted.");
            await pickingForm.Locator("button[type='submit']").ClickAsync();
            var connectivityBanner = page.Locator("[data-wms-connectivity]");
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-wms-connectivity]')?.dataset.wmsConnectivityState === 'blocked'");
            Assert.Contains(
                "This action needs a connection. Nothing was submitted.",
                await connectivityBanner.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.EndsWith("/Picking", new Uri(page.Url).AbsolutePath, StringComparison.Ordinal);
            Assert.DoesNotContain("success", await page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
            using (var sessionScope = factory.Services.CreateScope())
            {
                var context = sessionScope.ServiceProvider.GetRequiredService<WmsDbContext>();
                Assert.Equal(10m, await ReadOnHandAsync(context, firstScenario.ItemId, firstLocationId));
                Assert.Equal(1m, await ReadOnHandAsync(context, firstScenario.ItemId, destinationLocationId));
            }

            Volatile.Write(ref expectedOfflineFailure, 0);
            await page.Context.SetOfflineAsync(false);

            activeCase = "actor-session-isolation-after-logout";
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Receiving/Receive", "en-US"));
            await page.Locator("#receiving-session-active").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-scan')?.disabled === false");
            await page.Locator("#receiving-session-scan").FocusAsync();
            await page.Context.SetOfflineAsync(true);
            Volatile.Write(ref expectedOfflineFailure, 1);
            await page.Keyboard.TypeAsync("UNSENT-PRIVATE-SCAN");
            await page.Keyboard.PressAsync("Enter");
            await page.Locator("#receiving-session-queue-status").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            await page.WaitForFunctionAsync(
                "() => document.getElementById('receiving-session-queue-status')?.textContent.trim().endsWith('1') === true");
            var actorAQueueKey = $"warecommand.receiving.queue.{Uri.EscapeDataString(firstActorStorageKey!)}.{sessionId}";
            Assert.True(await page.EvaluateAsync<bool>(
                "key => localStorage.getItem(key)?.includes('UNSENT-PRIVATE-SCAN') === true",
                actorAQueueKey));
            Volatile.Write(ref expectedOfflineFailure, 0);
            await page.Context.SetOfflineAsync(false);
            var logoutButton = page.Locator("form[action='/Account/Logout'] button[type='submit']");
            if (!await logoutButton.IsVisibleAsync())
            {
                await page.Locator("button.navbar-toggler[data-bs-target='#navbarNav']").ClickAsync();
                await logoutButton.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible
                });
            }

            await logoutButton.ClickAsync();
            await page.WaitForURLAsync(url => url.Contains("/Account/Login", StringComparison.Ordinal));
            await LoginAsync(page, host.Origin, secondActor.UserName!);
            activeCase = "actor-b-session-isolation-after-logout";
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Receiving/Receive", "en-US"));
            Assert.True(await page.Locator("#receiving-session-active").EvaluateAsync<bool>(
                "element => element.classList.contains('d-none')"));
            Assert.DoesNotContain("UNSENT-PRIVATE-SCAN", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
            Assert.DoesNotContain(firstScenario.ItemSku, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
            var secondActorStorageKey = await page.Locator("body").GetAttributeAsync("data-wms-actor-key");
            Assert.NotEqual(firstActorStorageKey, secondActorStorageKey);
            var secondActorActiveKey = $"warecommand.receiving.active.{Uri.EscapeDataString(secondActorStorageKey!)}";
            Assert.False(await page.EvaluateAsync<bool>(
                "key => localStorage.getItem(key) !== null",
                secondActorActiveKey));
            Assert.DoesNotContain(events, evidence =>
                evidence.Kind == "receiving-scan-request" &&
                evidence.CaseId == "actor-b-session-isolation-after-logout");

            activeCase = "actor-b-inventory-isolation";
            await page.GotoAsync(LocalizedUrl(
                host.Origin,
                "/Inventory?searchTerm=" + Uri.EscapeDataString(firstScenario.ItemSku),
                "en-US"));
            var actorBInventory = await page.Locator("body").InnerTextAsync();
            Assert.DoesNotContain(firstScenario.ItemSku, actorBInventory, StringComparison.Ordinal);
            await page.GotoAsync(LocalizedUrl(
                host.Origin,
                "/Inventory?searchTerm=" + Uri.EscapeDataString(secondScenario.ItemSku),
                "en-US"));
            Assert.Contains(secondScenario.ItemSku, await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

            activeCase = "service-worker-controlled-offline-fallback";
            await page.EvaluateAsync("async () => { await navigator.serviceWorker.ready; }");
            await page.ReloadAsync();
            await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");
            await page.Context.SetOfflineAsync(true);
            Volatile.Write(ref expectedOfflineFailure, 1);
            await page.GotoAsync(LocalizedUrl(host.Origin, "/Inventory", "en-US"));
            await page.Locator("#offline-title").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible
            });
            Assert.Contains("no inventory command was queued or accepted offline", await page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
            Volatile.Write(ref expectedOfflineFailure, 0);
            await page.Context.SetOfflineAsync(false);

            var unexpected = events.Where(evidence => !evidence.Expected).ToArray();
            Assert.Empty(unexpected);
        }
        catch (Exception exception)
        {
            await WriteFailureEvidenceAsync(page, events, activeCase, exception.GetType().Name);
            throw;
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync();
            }

            if (browser is not null)
            {
                await browser.CloseAsync();
            }

            playwright?.Dispose();
            factory.Dispose();
            await factory.DisposeDatabaseAsync();
        }
    }

    private static async Task LoginAsync(IPage page, string origin, string userName)
    {
        await page.GotoAsync(LocalizedUrl(origin, "/Account/Login", "en-US"));
        await page.Locator("#UserName").FillAsync(userName);
        await page.Locator("#Password").FillAsync(TestPassword);
        await page.Locator("form button[type='submit']").ClickAsync();
        await page.WaitForURLAsync(url =>
            !url.Contains("/Account/Login", StringComparison.Ordinal));
    }

    private static string LocalizedUrl(string origin, string route, string culture)
    {
        var separator = route.Contains('?') ? '&' : '?';
        return $"{origin}{route}{separator}culture={Uri.EscapeDataString(culture)}&ui-culture={Uri.EscapeDataString(culture)}";
    }

    private static async Task<bool> HasNoPageOverflowAsync(IPage page) =>
        await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth");

    private static async Task EnsureWarehouseSelectorVisibleAsync(IPage page)
    {
        var selector = page.Locator("#layout-warehouse");
        if (await selector.IsVisibleAsync())
        {
            return;
        }

        await page.Locator("button.navbar-toggler[data-bs-target='#navbarNav']").ClickAsync();
        await selector.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    private static async Task<decimal> ReadOnHandAsync(
        WmsDbContext context,
        int itemId,
        int locationId)
    {
        var stock = await context.Stock.AsNoTracking()
            .Where(stock => stock.ItemId == itemId && stock.LocationId == locationId)
            .ToListAsync();
        return stock.Sum(row => row.QuantityAvailable.Value);
    }

    private static bool IsExpectedOfflineFailure(string caseId, string path)
    {
        if (string.Equals(path, "/health/ready", StringComparison.OrdinalIgnoreCase))
        {
            return caseId is "offline-scan-queued-not-accepted" or
                "unsupported-offline-form-is-not-claimed-successful" or
                "actor-session-isolation-after-logout" or
                "service-worker-controlled-offline-fallback";
        }

        if (string.Equals(path, "/Inventory", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(caseId, "service-worker-controlled-offline-fallback", StringComparison.Ordinal);
        }

        return (caseId is "offline-scan-queued-not-accepted" or "actor-session-isolation-after-logout") &&
            path.Contains("/api/receiving/sessions/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith("/scans", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReceivingScanPath(string url) =>
        SafePath(url).Contains("/api/receiving/sessions/", StringComparison.OrdinalIgnoreCase) &&
        SafePath(url).EndsWith("/scans", StringComparison.OrdinalIgnoreCase);

    private static string SafePath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            uri.AbsolutePath,
            @"(/api/receiving/sessions/)\d+",
            "$1{sessionId}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string SafeRouteName(string route) =>
        route.Split('?', StringSplitOptions.RemoveEmptyEntries)[0].Trim('/').Replace('/', '-');

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Warehouse Management System.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the WareCommand solution root for the real browser host.");
    }

    private static async Task WriteFailureEvidenceAsync(
        IPage? page,
        IEnumerable<BrowserEvidenceEvent> events,
        string? caseId,
        string failureType)
    {
        var configuredDirectory = Environment.GetEnvironmentVariable(
            "WARECOMMAND_BROWSER_ARTIFACT_DIRECTORY");
        var artifactDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(Path.GetTempPath(), "warecommand-browser-evidence-" + Guid.NewGuid().ToString("N"))
            : configuredDirectory;
        Directory.CreateDirectory(artifactDirectory);
        var artifactName = "browser-" + Guid.NewGuid().ToString("N");
        var evidence = new
        {
            revision = Environment.GetEnvironmentVariable("WARECOMMAND_VERIFICATION_REVISION") ?? "local",
            caseId = caseId ?? "browser",
            failureType,
            capturedAtUtc = DateTimeOffset.UtcNow,
            events = events.TakeLast(200).ToArray(),
            excluded = new[] { "request and response bodies", "cookies", "authorization headers", "antiforgery tokens", "storage state", "raw Playwright traces" }
        };
        await File.WriteAllTextAsync(
            Path.Combine(artifactDirectory, artifactName + ".json"),
            JsonSerializer.Serialize(evidence, FailureEvidenceJsonOptions));
        if (page is null)
        {
            return;
        }

        if (events.Any(evidence =>
                evidence.CaseId == caseId &&
                evidence.Status is >= 500))
        {
            return;
        }

        try
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(artifactDirectory, artifactName + ".png"),
                FullPage = false,
                Mask = [page.Locator("input"), page.Locator("textarea")],
                MaskColor = "#000000"
            });
        }
        catch (PlaywrightException)
        {
            // The redacted JSON timeline remains usable when the browser has already closed.
        }
    }

    private sealed record BrowserEvidenceEvent(
        DateTimeOffset AtUtc,
        string CaseId,
        string Kind,
        string Path,
        int? Status,
        bool Expected);

    private sealed class BrowserTestHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly string _dataProtectionDirectory;

        private BrowserTestHost(Process process, string origin, string dataProtectionDirectory)
        {
            _process = process;
            Origin = origin;
            _dataProtectionDirectory = dataProtectionDirectory;
        }

        public string Origin { get; }

        public static async Task<BrowserTestHost> StartAsync(
            string repositoryRoot,
            string connectionString)
        {
            var port = GetAvailablePort();
            var origin = $"http://127.0.0.1:{port}";
            var webProject = Path.Combine(repositoryRoot, "Wms.ASP", "Wms.ASP.csproj");
            var dataProtectionDirectory = Path.Combine(
                Path.GetTempPath(),
                "warecommand-browser-keys-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataProtectionDirectory);

            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(webProject)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(webProject);
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--no-launch-profile");
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["ASPNETCORE_URLS"] = origin;
            startInfo.Environment["ConnectionStrings__DefaultConnection"] = connectionString;
            startInfo.Environment["Wms__DatabaseProvider"] = "PostgreSql";
            startInfo.Environment["Wms__SeedProfile"] = "None";
            startInfo.Environment["Wms__Jobs__Enabled"] = "false";
            startInfo.Environment["Wms__Backups__Enabled"] = "false";
            startInfo.Environment["HttpsRedirection__Enabled"] = "false";
            startInfo.Environment["Authentication__CookieSecure"] = "false";
            startInfo.Environment["Security__Antiforgery__SecureCookie"] = "false";
            startInfo.Environment["DataProtection__KeyDirectory"] = dataProtectionDirectory;
            startInfo.Environment["Logging__LogLevel__Default"] = "Warning";
            startInfo.Environment["Wms__Telemetry__Enabled"] = "false";
            startInfo.Environment["AllowedHosts"] = "*";

            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The real PostgreSQL browser host could not be started.");
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            var host = new BrowserTestHost(process, origin, dataProtectionDirectory);
            try
            {
                await host.WaitUntilReadyAsync();
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
            var tempRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var fullDirectory = Path.GetFullPath(_dataProtectionDirectory);
            if (fullDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullDirectory).StartsWith("warecommand-browser-keys-", StringComparison.Ordinal) &&
                Directory.Exists(fullDirectory))
            {
                Directory.Delete(fullDirectory, recursive: true);
            }
        }

        private async Task WaitUntilReadyAsync()
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException("The real PostgreSQL browser host exited during startup.");
                }

                try
                {
                    using var response = await client.GetAsync(Origin + "/health/live");
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // The process has not bound its loopback port yet.
                }
                catch (TaskCanceledException)
                {
                    // The health request timed out while the app was initializing.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            throw new TimeoutException("The real PostgreSQL browser host did not become ready within two minutes.");
        }

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}

public sealed class PostgreSqlBrowserFactAttribute : FactAttribute
{
    public PostgreSqlBrowserFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "WARECOMMAND_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Set WARECOMMAND_TEST_POSTGRES_CONNECTION or use scripts/verify-postgresql.ps1 -Group browser.";
        }
    }
}
