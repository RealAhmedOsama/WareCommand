using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Wms.Application.UseCases.Inventory;
using Wms.Application.UseCases.Items;
using Wms.Application.UseCases.Locations;
using Wms.Application.UseCases.Picking;
using Wms.Application.UseCases.Receiving;
using Wms.Application.UseCases.Reports;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.Services;
using Wms.WinForms.Forms;

namespace Wms.WinForms;

internal static class Program
{
    private static IHost? _host;

    public static IServiceProvider ServiceProvider =>
        _host?.Services ?? throw new InvalidOperationException("Service provider not initialized");

    /// <summary>
    ///     The main entry point for the application.
    /// </summary>
    [STAThread]
    private static async Task Main()
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
             .WriteTo.File("logs/wms-.txt", formatProvider: CultureInfo.InvariantCulture,
                 rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            // Build host
            _host = CreateHostBuilder().Build();

            // Initialize database
            await InitializeDatabaseAsync();

            // Configure Windows Forms
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.SystemAware);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            // Start the application
            var serviceProvider = _host.Services;
            var mainForm = serviceProvider.GetRequiredService<MainForm>();

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
            .ConfigureAppConfiguration((context, config) => { config.AddJsonFile("appsettings.json", false, true); })
            .ConfigureServices((context, services) =>
            {
                var configuration = context.Configuration;

                // Database
                services.AddDbContext<WmsDbContext>(options =>
                    options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

                // Repositories
                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<IItemRepository, ItemRepository>();
                services.AddScoped<ILocationRepository, LocationRepository>();
                services.AddScoped<IStockRepository, StockRepository>();
                services.AddScoped<IMovementRepository, MovementRepository>();

                // Domain Services
                services.AddScoped<IStockMovementService, StockMovementService>();

                // Application Services - Receiving
                services.AddScoped<IReceiveItemUseCase, ReceiveItemUseCase>();
                services.AddScoped<IPutawayUseCase, PutawayUseCase>();

                // Application Services - Items & Locations
                services.AddScoped<IGetItemsUseCase, GetItemsUseCase>();
                services.AddScoped<ICreateItemUseCase, CreateItemUseCase>();
                services.AddScoped<IUpdateItemUseCase, UpdateItemUseCase>();
                services.AddScoped<IDeleteItemUseCase, DeleteItemUseCase>();

                services.AddScoped<IGetLocationsUseCase, GetLocationsUseCase>();
                services.AddScoped<ICreateLocationUseCase, CreateLocationUseCase>();
                services.AddScoped<IUpdateLocationUseCase, UpdateLocationUseCase>();
                services.AddScoped<IDeleteLocationUseCase, DeleteLocationUseCase>();

                // Application Services - Inventory
                services.AddScoped<IGetStockUseCase, GetStockUseCase>();
                services.AddScoped<IStockAdjustmentUseCase, StockAdjustmentUseCase>();

                // Application Services - Picking
                services.AddScoped<IPickOrderUseCase, PickOrderUseCase>();

                // Application Services - Reports
                services.AddScoped<IMovementReportUseCase, MovementReportUseCase>();

                // Forms - Main Navigation
                services.AddTransient<MainForm>();
                services.AddTransient<DashboardForm>();

                // Forms - Operations
                services.AddTransient<ReceivingForm>();
                services.AddTransient<PutawayForm>();
                services.AddTransient<InventoryForm>();
                services.AddTransient<PickingForm>();

                // Forms - Management
                services.AddTransient<ItemManagementForm>();
                services.AddTransient<LocationManagementForm>();
                services.AddTransient<ReportsForm>();

                // Dialogs
                services.AddTransient<ItemEditDialog>();
                services.AddTransient<LocationEditDialog>();
            });
    }

    private static async Task InitializeDatabaseAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WmsDbContext>();

        await context.Database.EnsureCreatedAsync();

        // Seed initial data
        await SeedDataAsync(context);
    }

    private static async Task SeedDataAsync(WmsDbContext context)
    {
        // Check if already seeded
        if (await context.Warehouses.AnyAsync())
            return;

        // Create default warehouse
        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        // Create locations in proper order (parent first, then children)
        var receiveLocation = new Location("RECEIVE", "Receiving Dock", warehouse.Id);
        var zone1 = new Location("Z001", "Zone 1", warehouse.Id);

        // Set location properties
        receiveLocation.SetPickable(false); // Receive locations typically not pickable

        context.Locations.AddRange(receiveLocation, zone1);
        await context.SaveChangesAsync(); // Save parent locations first

        // Now create child locations with proper parent references
        var aisle1 = new Location("Z001-A001", "Zone 1 Aisle 1", warehouse.Id, zone1.Id);
        var aisle2 = new Location("Z001-A002", "Zone 1 Aisle 2", warehouse.Id, zone1.Id);

        context.Locations.AddRange(aisle1, aisle2);
        await context.SaveChangesAsync(); // Save aisle locations

        // Finally create bin locations
        var bin1 = new Location("Z001-A001-01", "Bin 01", warehouse.Id, aisle1.Id);
        var bin2 = new Location("Z001-A001-02", "Bin 02", warehouse.Id, aisle1.Id);

        context.Locations.AddRange(bin1, bin2);
        await context.SaveChangesAsync(); // Save bin locations

        // Create sample items with enhanced details
        var item1 = new Item("WIDGET-001", "Widget Type A", "EA");
        item1.UpdateDetails("Widget Type A", "High-quality widget for industrial applications");
        item1.AddBarcode(new Barcode("123456789012"));

        var item2 = new Item("GADGET-001", "Gadget Type B", "EA", true);
        item2.UpdateDetails("Gadget Type B", "Advanced gadget with lot tracking");
        item2.SetShelfLife(365); // 1 year shelf life
        item2.AddBarcode(new Barcode("234567890123"));

        var item3 = new Item("TOOL-001", "Professional Tool Set", "EA");
        item3.UpdateDetails("Professional Tool Set", "Complete professional tool kit");
        item3.AddBarcode(new Barcode("345678901234"));

        var item4 = new Item("PART-001", "Electronic Component", "EA", true);
        item4.UpdateDetails("Electronic Component", "Precision electronic component with lot control");
        item4.SetShelfLife(730); // 2 years shelf life
        item4.AddBarcode(new Barcode("456789012345"));

        // Add more sample items for better demo
        var item5 = new Item("CABLE-001", "Ethernet Cable 5ft", "EA");
        item5.UpdateDetails("Ethernet Cable 5ft", "CAT6 Ethernet cable, 5 feet length");
        item5.AddBarcode(new Barcode("567890123456"));

        var item6 = new Item("SENSOR-001", "Temperature Sensor", "EA", false, true);
        item6.UpdateDetails("Temperature Sensor", "Digital temperature sensor with serial tracking");
        item6.AddBarcode(new Barcode("678901234567"));

        context.Items.AddRange(item1, item2, item3, item4, item5, item6);
        await context.SaveChangesAsync();

        // Add some initial stock for demonstration
        var stockItems = new[]
        {
            new Stock(item1.Id, bin1.Id, new Quantity(50.0m)),
            new Stock(item1.Id, bin2.Id, new Quantity(25.0m)),
            new Stock(item3.Id, bin1.Id, new Quantity(15.0m)),
            new Stock(item5.Id, aisle1.Id, new Quantity(100.0m))
        };

        context.Stock.AddRange(stockItems);
        await context.SaveChangesAsync();

        Log.Information("Database seeded with initial data: 6 items, 6 locations, 4 stock records");
    }
}
