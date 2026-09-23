using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.B2bDocuments;
using Wms.Application.BulkExchange;
using Wms.Application.Connectors;
using Wms.Application.Identity;
using Wms.Application.Integrations;
using Wms.Application.Notifications;
using Wms.Application.WarehouseWork;
using Xunit;

namespace Wms.ASP.Tests;

public sealed class ModuleWiringFlowTests(WareCommandWebApplicationFactory factory)
    : IClassFixture<WareCommandWebApplicationFactory>
{
    [Fact]
    public async Task Local_module_contracts_are_composed_and_exposed_through_authorized_surfaces()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            Assert.NotNull(services.GetRequiredService<IConnectorService>());
            Assert.NotNull(services.GetRequiredService<IB2bDocumentService>());
            Assert.NotNull(services.GetRequiredService<IBulkImportService>());
            Assert.NotNull(services.GetRequiredService<IBulkExportService>());
            Assert.NotNull(services.GetRequiredService<IIntegrationEventWriter>());
            Assert.NotNull(services.GetRequiredService<IIntegrationInboxService>());
            Assert.NotNull(services.GetRequiredService<IIntegrationOutboxDispatcher>());
            Assert.NotNull(services.GetRequiredService<IEmailTransport>());
            Assert.NotNull(services.GetRequiredService<INotificationChannelHealthService>());
            Assert.Equal(2, services.GetServices<IConnectorAdapter>().Count());
            Assert.Equal(2, services.GetServices<INotificationChannelAdapter>().Count());
            Assert.Equal(4, services.GetServices<IWarehouseWorkCompletionHandler>().Count());
        }

        var administrator = await factory.CreateUserAsync(assignDefaultRole: false);
        await factory.AssignRoleAsync(administrator.Id, WmsRoleNames.Administrator);
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

        using var connectors = await client.GetAsync("/api/connectors");
        Assert.Equal(HttpStatusCode.OK, connectors.StatusCode);

        using var b2b = await client.GetAsync("/api/b2b/capabilities");
        Assert.Equal(HttpStatusCode.OK, b2b.StatusCode);
        Assert.Contains(WmsB2bStandards.Canonical, await b2b.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var bulk = await client.GetAsync("/api/bulk/capabilities");
        Assert.Equal(HttpStatusCode.OK, bulk.StatusCode);
        Assert.Contains(WmsBulkImportTypes.ItemsV1, await bulk.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var integrations = await client.GetAsync("/api/integrations/capabilities");
        Assert.Equal(HttpStatusCode.OK, integrations.StatusCode);
        using var integrationJson = JsonDocument.Parse(await integrations.Content.ReadAsStringAsync());
        Assert.True(integrationJson.RootElement.TryGetProperty("outbox", out _));
        var webhook = integrationJson.RootElement.GetProperty("webhook");
        Assert.Equal("Disabled", webhook.GetProperty("status").GetString());
        Assert.False(webhook.GetProperty("enabled").GetBoolean());
        Assert.False(webhook.GetProperty("configured").GetBoolean());
        Assert.False(webhook.GetProperty("verified").GetBoolean());

        using var notifications = await client.GetAsync("/api/notifications/capabilities");
        Assert.Equal(HttpStatusCode.OK, notifications.StatusCode);
        using var notificationJson = JsonDocument.Parse(await notifications.Content.ReadAsStringAsync());
        Assert.Equal(
            "Disabled",
            notificationJson.RootElement.GetProperty("email").GetProperty("status").GetString());
        Assert.Equal(
            "Disabled",
            notificationJson.RootElement.GetProperty("webhook").GetProperty("status").GetString());
    }
}
