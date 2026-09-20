using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wms.Application.Context;
using Wms.Application.DependencyInjection;
using Wms.Application.Identity;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;
using Wms.Infrastructure.Identity;
using Wms.Infrastructure.Logging;
using Wms.WinForms.Forms;

namespace Wms.WinForms;

internal static class Program
{
    private static IHost? _host;

    public static IServiceProvider ServiceProvider =>
        _host?.Services ?? throw new InvalidOperationException("Service provider not initialized");

    [STAThread]
    private static async Task Main()
    {
        Log.Logger = WmsLogging.CreateBootstrapLogger("Desktop").CreateLogger();

        try
        {
            _host = CreateHostBuilder().Build();
            await InitializeDatabaseAsync();

            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            using var loginScope = _host.Services.CreateScope();
            using var loginForm = loginScope.ServiceProvider.GetRequiredService<LoginForm>();
            if (loginForm.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            var logger = _host.Services
                .GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                .CreateLogger("Wms.WinForms.Program");
            var currentUser = _host.Services.GetRequiredService<ICurrentUser>();
            var operationContextAccessor = _host.Services
                .GetRequiredService<IWmsOperationContextAccessor>();
            using var desktopScope = WmsLogging.BeginOperation(
                logger,
                operationContextAccessor,
                new WmsOperationContext(
                    WmsExecutionIdentifiers.NewCorrelationId(),
                    WmsExecutionIdentifiers.NewOperationId(),
                    "Desktop",
                    "desktop.session",
                    UserId: currentUser.UserId));

            var mainForm = _host.Services.GetRequiredService<MainForm>();
            System.Windows.Forms.Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .UseSerilog((context, loggerConfiguration) =>
                WmsLogging.Configure(
                    loggerConfiguration,
                    context.Configuration,
                    context.HostingEnvironment))
            .ConfigureAppConfiguration((_, config) => config.AddJsonFile("appsettings.json", false, true))
            .ConfigureServices((context, services) =>
            {
                services.AddWmsLogging(context.Configuration);
                var databaseProvider = WmsDatabaseProviderParser.Parse(
                    context.Configuration["Wms:DatabaseProvider"]);
                services.AddWmsInfrastructure(
                    context.Configuration.GetConnectionString("DefaultConnection"),
                    databaseProvider);
                services.AddWmsDesktopIdentity();
                services.AddWmsApplication();

                services.AddTransient<LoginForm>();
                services.AddTransient<MainForm>();
                services.AddTransient<DashboardForm>();
                services.AddTransient<ReceivingForm>();
                services.AddTransient<PutawayForm>();
                services.AddTransient<InventoryForm>();
                services.AddTransient<PickingForm>();
                services.AddTransient<ItemManagementForm>();
                services.AddTransient<LocationManagementForm>();
                services.AddTransient<ReportsForm>();
                services.AddTransient<ItemEditDialog>();
                services.AddTransient<LocationEditDialog>();
            });
    }

    private static async Task InitializeDatabaseAsync()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<IWmsDatabaseInitializer>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var seedProfile = WmsSeedProfileResolver.Resolve(
            configuration["Wms:SeedProfile"],
            environment.IsDevelopment());
        await initializer.InitializeAsync(seedProfile);

        var authorizationBootstrapper = scope.ServiceProvider
            .GetRequiredService<WmsAuthorizationBootstrapper>();
        await authorizationBootstrapper.EnsureRolesAndPermissionsAsync();
    }
}
