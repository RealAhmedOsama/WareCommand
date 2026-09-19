using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Database;

public sealed class WmsDatabaseInitializer(
    WmsDbContext context,
    WmsDatabaseOptions databaseOptions,
    ILogger<WmsDatabaseInitializer> logger) : IWmsDatabaseInitializer
{
    public async Task InitializeAsync(
        WmsSeedProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (databaseOptions.Provider == WmsDatabaseProvider.PostgreSql)
            {
                var pendingMigrations = (await context.Database
                        .GetPendingMigrationsAsync(cancellationToken))
                    .ToArray();
                if (pendingMigrations.Length > 0)
                {
                    throw new InvalidOperationException(
                        "The PostgreSQL schema is not current. Apply checked-in EF migrations before starting WareCommand. " +
                        $"Pending migrations: {string.Join(", ", pendingMigrations)}.");
                }
            }
            else
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }

            if (profile == WmsSeedProfile.WebDemo)
            {
                await SeedWebDemoAsync(cancellationToken);
            }
            else
            {
                await SeedDesktopDemoAsync(cancellationToken);
            }

            logger.LogInformation("Database initialized successfully using {SeedProfile} seed profile", profile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database using {SeedProfile}", profile);
            throw;
        }
    }

    private async Task SeedWebDemoAsync(CancellationToken cancellationToken)
    {
        if (await context.Items.AnyAsync(cancellationToken) ||
            await context.Locations.AnyAsync(cancellationToken))
        {
            return;
        }

        var warehouse = new Warehouse("Main Warehouse", "MAIN");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken);

        var receivingLocation = new Location("RECEIVING", "Receiving Area", warehouse.Id);
        receivingLocation.SetReceivable(true);
        receivingLocation.SetPickable(false);

        var storageLocation = new Location("A001", "Storage Area A1", warehouse.Id);
        storageLocation.SetReceivable(true);
        storageLocation.SetPickable(true);

        var shippingLocation = new Location("SHIPPING", "Shipping Area", warehouse.Id);
        shippingLocation.SetReceivable(false);
        shippingLocation.SetPickable(true);

        context.Locations.AddRange(receivingLocation, storageLocation, shippingLocation);

        var item1 = new Item("WIDGET-001", "Standard Widget", "EA");
        var item2 = new Item("GADGET-001", "Premium Gadget", "EA", true);
        var item3 = new Item("TOOL-001", "Professional Tool", "EA", requiresSerial: true);

        context.Items.AddRange(item1, item2, item3);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Web demo seed data created successfully");
    }

    private async Task SeedDesktopDemoAsync(CancellationToken cancellationToken)
    {
        if (await context.Warehouses.AnyAsync(cancellationToken))
        {
            return;
        }

        var warehouse = new Warehouse("MAIN", "Main Warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken);

        var receiveLocation = new Location("RECEIVE", "Receiving Dock", warehouse.Id);
        var zone1 = new Location("Z001", "Zone 1", warehouse.Id);
        receiveLocation.SetPickable(false);

        context.Locations.AddRange(receiveLocation, zone1);
        await context.SaveChangesAsync(cancellationToken);

        var aisle1 = new Location("Z001-A001", "Zone 1 Aisle 1", warehouse.Id, zone1.Id);
        var aisle2 = new Location("Z001-A002", "Zone 1 Aisle 2", warehouse.Id, zone1.Id);

        context.Locations.AddRange(aisle1, aisle2);
        await context.SaveChangesAsync(cancellationToken);

        var bin1 = new Location("Z001-A001-01", "Bin 01", warehouse.Id, aisle1.Id);
        var bin2 = new Location("Z001-A001-02", "Bin 02", warehouse.Id, aisle1.Id);

        context.Locations.AddRange(bin1, bin2);
        await context.SaveChangesAsync(cancellationToken);

        var item1 = new Item("WIDGET-001", "Widget Type A", "EA");
        item1.UpdateDetails("Widget Type A", "High-quality widget for industrial applications");
        item1.AddBarcode(new Barcode("123456789012"));

        var item2 = new Item("GADGET-001", "Gadget Type B", "EA", true);
        item2.UpdateDetails("Gadget Type B", "Advanced gadget with lot tracking");
        item2.SetShelfLife(365);
        item2.AddBarcode(new Barcode("234567890123"));

        var item3 = new Item("TOOL-001", "Professional Tool Set", "EA");
        item3.UpdateDetails("Professional Tool Set", "Complete professional tool kit");
        item3.AddBarcode(new Barcode("345678901234"));

        var item4 = new Item("PART-001", "Electronic Component", "EA", true);
        item4.UpdateDetails("Electronic Component", "Precision electronic component with lot control");
        item4.SetShelfLife(730);
        item4.AddBarcode(new Barcode("456789012345"));

        var item5 = new Item("CABLE-001", "Ethernet Cable 5ft", "EA");
        item5.UpdateDetails("Ethernet Cable 5ft", "CAT6 Ethernet cable, 5 feet length");
        item5.AddBarcode(new Barcode("567890123456"));

        var item6 = new Item("SENSOR-001", "Temperature Sensor", "EA", false, true);
        item6.UpdateDetails("Temperature Sensor", "Digital temperature sensor with serial tracking");
        item6.AddBarcode(new Barcode("678901234567"));

        context.Items.AddRange(item1, item2, item3, item4, item5, item6);
        await context.SaveChangesAsync(cancellationToken);

        var stockItems = new[]
        {
            new Stock(item1.Id, bin1.Id, new Quantity(50.0m)),
            new Stock(item1.Id, bin2.Id, new Quantity(25.0m)),
            new Stock(item3.Id, bin1.Id, new Quantity(15.0m)),
            new Stock(item5.Id, aisle1.Id, new Quantity(100.0m))
        };

        context.Stock.AddRange(stockItems);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Desktop demo seed data created successfully");
    }
}
