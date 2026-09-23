using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Wms.Application.Administration;
using Wms.Application.AnomalyDetection;
using Wms.Application.ApiClients;
using Wms.Application.Approvals;
using Wms.Application.Attachments;
using Wms.Application.Auditing;
using Wms.Application.B2bDocuments;
using Wms.Application.BulkExchange;
using Wms.Application.Connectors;
using Wms.Application.Context;
using Wms.Application.Customers;
using Wms.Application.Dashboard;
using Wms.Application.Forecasting;
using Wms.Application.Idempotency;
using Wms.Application.Identification;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Integrations;
using Wms.Application.Inventory;
using Wms.Application.InventoryStatuses;
using Wms.Application.Items;
using Wms.Application.Jobs;
using Wms.Application.Labels;
using Wms.Application.LicensePlates;
using Wms.Application.Locations;
using Wms.Application.Lots;
using Wms.Application.Notifications;
using Wms.Application.Outbound;
using Wms.Application.Packing;
using Wms.Application.Purchasing;
using Wms.Application.Putaway;
using Wms.Application.Quality;
using Wms.Application.Receiving;
using Wms.Application.Recommendations;
using Wms.Application.Reporting;
using Wms.Application.ReportingAssistant;
using Wms.Application.Retention;
using Wms.Application.Returns;
using Wms.Application.SalesOrders;
using Wms.Application.SerialNumbers;
using Wms.Application.Settings;
using Wms.Application.Shipping;
using Wms.Application.SupplierReturns;
using Wms.Application.Suppliers;
using Wms.Application.Transfers;
using Wms.Application.Units;
using Wms.Application.ValueAddedServices;
using Wms.Application.Warehouses;
using Wms.Application.WarehouseWork;
using Wms.Application.Workforce;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Administration;
using Wms.Infrastructure.AnomalyDetection;
using Wms.Infrastructure.ApiClients;
using Wms.Infrastructure.Approvals;
using Wms.Infrastructure.Attachments;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.B2bDocuments;
using Wms.Infrastructure.BulkExchange;
using Wms.Infrastructure.Connectors;
using Wms.Infrastructure.Customers;
using Wms.Infrastructure.Dashboard;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.Forecasting;
using Wms.Infrastructure.Identification;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Inbound;
using Wms.Infrastructure.Integrations;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.InventoryStatuses;
using Wms.Infrastructure.Items;
using Wms.Infrastructure.Jobs;
using Wms.Infrastructure.Labels;
using Wms.Infrastructure.LicensePlates;
using Wms.Infrastructure.Locations;
using Wms.Infrastructure.Logging;
using Wms.Infrastructure.Lots;
using Wms.Infrastructure.Notifications;
using Wms.Infrastructure.Outbound;
using Wms.Infrastructure.Packing;
using Wms.Infrastructure.Purchasing;
using Wms.Infrastructure.Putaway;
using Wms.Infrastructure.Quality;
using Wms.Infrastructure.Receiving;
using Wms.Infrastructure.Recommendations;
using Wms.Infrastructure.Reporting;
using Wms.Infrastructure.ReportingAssistant;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Retention;
using Wms.Infrastructure.Returns;
using Wms.Infrastructure.SalesOrders;
using Wms.Infrastructure.SerialNumbers;
using Wms.Infrastructure.Services;
using Wms.Infrastructure.Settings;
using Wms.Infrastructure.Shipping;
using Wms.Infrastructure.SupplierReturns;
using Wms.Infrastructure.Suppliers;
using Wms.Infrastructure.Telemetry;
using Wms.Infrastructure.Transfers;
using Wms.Infrastructure.Units;
using Wms.Infrastructure.ValueAddedServices;
using Wms.Infrastructure.Warehouses;
using Wms.Infrastructure.WarehouseWork;
using Wms.Infrastructure.Workforce;

namespace Wms.Infrastructure.DependencyInjection;

/// <summary>
/// Registers persistence and infrastructure adapters for the composition roots.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWmsInfrastructure(
        this IServiceCollection services,
        string? connectionString,
        WmsDatabaseProvider provider = WmsDatabaseProvider.PostgreSql,
        IConfiguration? configuration = null)
    {
        services.AddPersistence(connectionString, provider);
        services.AddDataProtection();
        services.AddWebhookDeliveryTransport(configuration);
        services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        services.AddAttachmentInfrastructure(configuration);
        services.AddInventoryInfrastructure();
        services.AddRecommendationInfrastructure(configuration);
        services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();
        services.AddScoped<IAccountDirectory, AccountDirectory>();
        services.AddScoped<IWarehouseAccessService, WarehouseAccessService>();
        services.AddScoped<IUserAccessDirectory, UserAccessDirectory>();
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IWmsOperationContextAccessor, WmsOperationContextAccessor>();
        services.TryAddScoped<IRequestContext, WmsRequestContext>();
        services.TryAddScoped<IWarehouseContext, WmsWarehouseContext>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IAdministrationService, AdministrationService>();
        services.AddScoped<IApiClientContextAccessor, ApiClientContextAccessor>();
        services.AddScoped<IApiClientCredentialService, ApiClientCredentialService>();
        services.AddScoped<IIntegrationEventWriter, IntegrationEventWriter>();
        services.AddScoped<IIntegrationInboxService, IntegrationInboxService>();
        services.AddScoped<IIntegrationOutboxDispatcher, IntegrationOutboxDispatcher>();
        services.AddScoped<IWebhookSubscriptionService, WebhookSubscriptionService>();
        services.AddScoped<IWebhookSecretProtector, DataProtectionWebhookSecretProtector>();
        services.AddScoped<IConnectorService, ConnectorService>();
        services.AddScoped<IB2bDocumentService, B2bDocumentService>();
        services.AddScoped<IBulkCsvParser, BulkCsvParser>();
        services.AddScoped<IBulkImportService, BulkImportService>();
        services.AddScoped<IBulkExportService, BulkExportService>();
        services.AddScoped<IBulkImportRowValidator, RequiredColumnBulkImportValidator>();
        services.AddScoped<IApprovalRoleDirectory, ApprovalRoleDirectory>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<INotificationRecipientDirectory, NotificationRecipientDirectory>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationDeliveryService, NotificationDeliveryService>();
        services.AddScoped<INotificationChannelAdapter, EmailNotificationAdapter>();
        services.AddScoped<INotificationChannelAdapter, IntegrationWebhookNotificationAdapter>();
        services.AddScoped<INotificationChannelHealthService, NotificationChannelHealthService>();
        services.AddScoped<IWmsJobExecutionStore, WmsJobExecutionStore>();
        services.AddScoped<IRetentionService, RetentionService>();
        services.AddScoped<WmsJobHandlerCatalog>();
        services.AddScoped<WmsExpiryAlertJob>();
        services.AddScoped<WmsLowStockAlertJob>();
        services.AddScoped<WmsReportGenerationJob>();
        services.AddScoped<WmsIntegrationRetryJob>();
        services.AddScoped<WmsCleanupJob>();
        services.AddScoped<WmsDatabaseBackupJob>();
        services.AddScoped<WmsInventoryClassificationRecalculationJob>();
        services.AddScoped<WmsSlottingAnalysisJob>();
        services.AddScoped<WmsCycleCountGenerationJob>();
        services.AddScoped<WmsReplenishmentGenerationJob>();
        services.AddScoped<WmsWavePlanningJob>();
        services.AddScoped<WmsInventoryHealthCheckJob>();
        services.AddScoped<WmsInventoryReconciliationJob>();
        services.AddScoped<WmsForecastRecalculationJob>();
        services.AddScoped<WmsAnomalyDetectionJob>();
        services.AddSingleton<WmsSettingsCache>();
        services.AddScoped<IWmsSettingsService, WmsSettingsService>();
        services.AddScoped<IWarehouseManagementService, WarehouseManagementService>();
        services.AddScoped<ILocationManagementService, LocationManagementService>();
        services.AddScoped<IItemManagementService, ItemManagementService>();
        services.AddScoped<ISupplierManagementService, SupplierManagementService>();
        services.AddScoped<ICustomerManagementService, CustomerManagementService>();
        services.AddScoped<ISalesOrderService, SalesOrderService>();
        services.AddScoped<ISalesOrderAllocationService, SalesOrderAllocationService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IAdvanceShippingNoticeService, AdvanceShippingNoticeService>();
        services.AddScoped<ICrossDockService, CrossDockService>();
        services.AddScoped<IInboundExceptionService, InboundExceptionService>();
        services.AddScoped<IOutboundExceptionService, OutboundExceptionService>();
        services.AddScoped<IWaveService, WaveService>();
        services.AddScoped<IPickingStrategyService, PickingStrategyService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IQualityInspectionService, QualityInspectionService>();
        services.AddScoped<IWarehouseWorkService, WarehouseWorkService>();
        services.AddScoped<IWarehouseWorkAssignmentEligibilityService, WarehouseWorkAssignmentEligibilityService>();
        services.AddScoped<IWorkforceService, WorkforceService>();
        services.AddScoped<ISlottingService, SlottingService>();
        services.AddScoped<IWarehouseWorkCompletionHandler, PutawayWarehouseWorkCompletionHandler>();
        services.AddScoped<IWarehouseWorkCompletionHandler, PickWarehouseWorkCompletionHandler>();
        services.AddScoped<IWarehouseWorkCompletionHandler, ReplenishmentWarehouseWorkCompletionHandler>();
        services.AddScoped<IWarehouseWorkCompletionHandler, SupplierReturnWarehouseWorkCompletionHandler>();
        services.AddScoped<IPackingService, PackingService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<ISupplierReturnService, SupplierReturnService>();
        services.AddScoped<IPutawayRuleService, PutawayRuleService>();
        services.AddScoped<IReceivingExecutionService, ReceivingExecutionService>();
        services.AddScoped<IIdentificationRegistry, IdentificationRegistry>();
        services.AddScoped<IIdentificationService, IdentificationService>();
        services.AddScoped<ILotService, LotService>();
        services.AddScoped<ISerialNumberService, SerialNumberService>();
        services.AddScoped<IInventoryStatusService, InventoryStatusService>();
        services.AddScoped<ILicensePlateService, LicensePlateService>();
        services.AddScoped<IUnitOfMeasureManagementService, UnitOfMeasureService>();
        services.AddScoped<IItemQuantityConversionService, UnitOfMeasureService>();
        services.AddScoped<IReportQueryService, OperationalReportQueryService>();
        services.AddScoped<IReportingAssistantService, ReportingAssistantService>();
        services.AddScoped<IForecastingService, ForecastingService>();
        services.AddScoped<IAnomalyDetectionService, AnomalyDetectionService>();
        services.AddScoped<ILabelTemplateService, WmsLabelTemplateService>();
        services.AddScoped<ILabelPrintService, WmsLabelPrintService>();
        services.AddScoped<WmsAuthorizationBootstrapper>();
        services.AddDatabaseInitialization();
        services.AddSingleton<WmsDbCommandMetricsInterceptor>();

        return services;
    }

    private static IServiceCollection AddAttachmentInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration)
    {
        var options = AttachmentStorageOptions.From(configuration);
        if (!string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "An external attachment storage provider is selected, but no provider adapter is registered. " +
                "Register an IAttachmentStorage implementation before starting the host.");
        }

        services.AddSingleton(options);
        services.AddSingleton<IAttachmentStorage, LocalAttachmentStorage>();
        services.AddSingleton<IAttachmentScanner, NoOpAttachmentScanner>();
        services.AddScoped<IAttachmentReferenceAccessService, AttachmentReferenceAccessService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        return services;
    }

    public static IServiceCollection AddWmsDesktopIdentity(this IServiceCollection services)
    {
        services
            .AddIdentityCore<WmsUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<WmsDbContext>();

        services.AddSingleton<DesktopUserSession>();
        services.AddSingleton<Wms.Application.Identity.ICurrentUser>(
            provider => provider.GetRequiredService<DesktopUserSession>());
        services.AddScoped<IRequestContext>(_ => new WmsRequestContext("Desktop"));
        services.AddScoped<IDesktopAuthenticationService, DesktopAuthenticationService>();
        return services;
    }

    private static IServiceCollection AddPersistence(
        this IServiceCollection services,
        string? connectionString,
        WmsDatabaseProvider provider)
    {
        if (provider == WmsDatabaseProvider.PostgreSql && string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required when Wms:DatabaseProvider is PostgreSql.");
        }

        var resolvedConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? "Data Source=warehouse.db"
            : connectionString;

        services.AddSingleton(new WmsDatabaseOptions(
            provider,
            connectionStringConfigured: provider == WmsDatabaseProvider.Sqlite ||
                !string.IsNullOrWhiteSpace(connectionString)));
        services.AddDbContext<WmsDbContext>((serviceProvider, options) =>
        {
            if (provider == WmsDatabaseProvider.PostgreSql)
            {
                options.UseNpgsql(
                    resolvedConnectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(WmsDbContext).Assembly.FullName));
            }
            else
            {
                options.UseSqlite(resolvedConnectionString);
            }

            options.AddInterceptors(
                serviceProvider.GetRequiredService<WmsDbCommandMetricsInterceptor>());
        });
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ILotRepository, LotRepository>();
        services.AddScoped<ISerialNumberRepository, SerialNumberRepository>();
        services.AddScoped<IInventoryStatusRepository, InventoryStatusRepository>();
        services.AddScoped<ILicensePlateRepository, LicensePlateRepository>();
        services.AddScoped<IInventoryBalanceRepository, InventoryBalanceRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<IInventoryCommandIdempotencyRepository, InventoryCommandIdempotencyRepository>();
        services.AddScoped<IInventoryReservationRepository, InventoryReservationRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IMovementRepository, MovementRepository>();

        return services;
    }

    private static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IStockMovementService, StockMovementService>();
        services.AddScoped<IInventoryLedgerService, InventoryLedgerService>();
        services.AddScoped<IInventoryReservationService, InventoryReservationService>();
        services.AddScoped<IInventoryCommandIdempotencyService, InventoryCommandIdempotencyService>();
        services.AddScoped<IInventoryInquiryService, InventoryInquiryService>();
        services.AddScoped<IInventoryOwnershipService, InventoryOwnershipService>();
        services.AddScoped<IInventoryOwnershipReportService, InventoryOwnershipReportService>();
        services.AddScoped<IInventoryReplenishmentPolicyService, InventoryReplenishmentPolicyService>();
        services.AddScoped<IReplenishmentExecutionService, ReplenishmentExecutionService>();
        services.AddScoped<IInventoryClassificationService, InventoryClassificationService>();
        services.AddScoped<IInventoryAllocationStrategyService, InventoryAllocationStrategyService>();
        services.AddScoped<IInventoryDispositionService, InventoryDispositionService>();
        services.AddScoped<ICycleCountService, CycleCountService>();
        services.AddScoped<IInventoryReconciliationService, InventoryReconciliationService>();
        services.AddScoped<IDashboardReadService, DashboardReadService>();
        services.AddScoped<IValueAddedService, ValueAddedService>();
        return services;
    }

    private static IServiceCollection AddRecommendationInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration)
    {
        var options = services.AddOptions<RecommendationGovernanceOptions>();
        if (configuration is not null)
        {
            options.Bind(configuration.GetSection(RecommendationGovernanceOptions.SectionName));
        }

        options
            .Validate(value =>
                    value.MaximumProposalsPerRequest is >= 1 and <= 200 &&
                    value.ProviderTimeoutSeconds is >= 1 and <= 60 &&
                    !string.IsNullOrWhiteSpace(value.ProviderName) &&
                    value.ProviderName.Trim().Length <= 100,
                "Recommendation budgets, provider timeout, or provider name are invalid.")
            .ValidateOnStart();

        services.AddSingleton<IRecommendationDraftProvider, DeterministicReplenishmentDraftProvider>();
        services.AddScoped<IRecommendationCommandAdapter, ReplenishmentRecommendationCommandAdapter>();
        services.AddScoped<IRecommendationGovernanceService, RecommendationGovernanceService>();
        return services;
    }

    private static IServiceCollection AddWebhookDeliveryTransport(
        this IServiceCollection services,
        IConfiguration? configuration)
    {
        var enabled = configuration?.GetValue<bool>(
            WebhookDeliveryTransportOptions.SectionName + ":Enabled") == true;
        if (!enabled)
        {
            services.AddScoped<IWebhookDeliveryTransport, UnconfiguredWebhookDeliveryTransport>();
            return services;
        }

        services.AddOptions<WebhookDeliveryTransportOptions>()
            .Bind(configuration!.GetSection(WebhookDeliveryTransportOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<WebhookDeliveryTransportOptions>,
            WebhookDeliveryTransportOptionsValidator>();
        services.AddSingleton<IWebhookDnsResolver, SystemWebhookDnsResolver>();
        services.AddSingleton<WebhookDestinationPolicy>();
        services.AddHttpClient<HttpWebhookDeliveryTransport>((serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<WebhookDeliveryTransportOptions>>()
                    .Value;
                client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            })
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<WebhookDeliveryTransportOptions>>()
                    .Value;
                return new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectCallback = serviceProvider
                        .GetRequiredService<WebhookDestinationPolicy>()
                        .ConnectAsync,
                    ConnectTimeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
                    MaxConnectionsPerServer = 32,
                    MaxResponseHeadersLength = 16,
                    PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                    UseCookies = false,
                    UseProxy = false
                };
            });
        services.AddScoped<IWebhookDeliveryTransport>(serviceProvider =>
            serviceProvider.GetRequiredService<HttpWebhookDeliveryTransport>());
        return services;
    }

    private static IServiceCollection AddDatabaseInitialization(this IServiceCollection services)
    {
        services.AddScoped<IWmsSeedService, WmsSeedService>();
        services.AddScoped<IWmsDatabaseInitializer, WmsDatabaseInitializer>();
        return services;
    }
}
