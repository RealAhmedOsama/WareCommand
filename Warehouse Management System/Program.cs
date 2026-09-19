using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wms.Application.DependencyInjection;
using Wms.Infrastructure.Database;
using Wms.Infrastructure.DependencyInjection;
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
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                "logs/wms-.txt",
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            _host = CreateHostBuilder().Build();
            await InitializeDatabaseAsync();

            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

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
            .UseSerilog()
            .ConfigureAppConfiguration((_, config) => config.AddJsonFile("appsettings.json", false, true))
            .ConfigureServices((context, services) =>
            {
                var databaseProvider = WmsDatabaseProviderParser.Parse(
                    context.Configuration["Wms:DatabaseProvider"]);
                services.AddWmsInfrastructure(
                    context.Configuration.GetConnectionString("DefaultConnection"),
                    databaseProvider);
                services.AddWmsApplication();

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
        await initializer.InitializeAsync(WmsSeedProfile.DesktopDemo);
    }
}
