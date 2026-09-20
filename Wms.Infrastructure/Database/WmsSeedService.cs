using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Application.Settings;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Auditing;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Settings;

namespace Wms.Infrastructure.Database;

public sealed class WmsSeedService(WmsDbContext context, IClock? clock = null) : IWmsSeedService
{
    private readonly IClock _clock = clock ?? new SystemClock();
    private static readonly SeedLocation[] ReferenceLocations =
    [
        new("RECEIVE", "Receiving Dock", ParentCode: null, IsPickable: false, IsReceivable: true),
        new("Z001", "Zone 1", ParentCode: null, IsPickable: true, IsReceivable: true)
    ];

    private static readonly SeedLocation[] DemoLocations =
    [
        new("Z001-A001", "Zone 1 Aisle 1", "Z001", IsPickable: true, IsReceivable: true),
        new("Z001-A002", "Zone 1 Aisle 2", "Z001", IsPickable: true, IsReceivable: true),
        new("Z001-A001-01", "Bin 01", "Z001-A001", IsPickable: true, IsReceivable: true),
        new("Z001-A001-02", "Bin 02", "Z001-A001", IsPickable: true, IsReceivable: true)
    ];

    private static readonly SeedItem[] DemoItems =
    [
        new(
            "WIDGET-001",
            "Widget Type A",
            "High-quality widget for industrial applications",
            "EA",
            RequiresLot: false,
            RequiresSerial: false,
            ShelfLifeDays: 0,
            ["123456789012"]),
        new(
            "GADGET-001",
            "Gadget Type B",
            "Advanced gadget with lot tracking",
            "EA",
            RequiresLot: true,
            RequiresSerial: false,
            ShelfLifeDays: 365,
            ["234567890123"]),
        new(
            "TOOL-001",
            "Professional Tool Set",
            "Complete professional tool kit",
            "EA",
            RequiresLot: false,
            RequiresSerial: false,
            ShelfLifeDays: 0,
            ["345678901234"]),
        new(
            "PART-001",
            "Electronic Component",
            "Precision electronic component with lot control",
            "EA",
            RequiresLot: true,
            RequiresSerial: false,
            ShelfLifeDays: 730,
            ["456789012345"]),
        new(
            "CABLE-001",
            "Ethernet Cable 5ft",
            "CAT6 Ethernet cable, 5 feet length",
            "EA",
            RequiresLot: false,
            RequiresSerial: false,
            ShelfLifeDays: 0,
            ["567890123456"]),
        new(
            "SENSOR-001",
            "Temperature Sensor",
            "Digital temperature sensor with serial tracking",
            "EA",
            RequiresLot: false,
            RequiresSerial: true,
            ShelfLifeDays: 0,
            ["678901234567"])
    ];

    private static readonly SeedStock[] DemoStock =
    [
        new("WIDGET-001", "Z001-A001-01", 50.0m),
        new("WIDGET-001", "Z001-A001-02", 25.0m),
        new("TOOL-001", "Z001-A001-01", 15.0m),
        new("CABLE-001", "Z001-A001", 100.0m)
    ];

    private static readonly SeedUnit[] StandardUnits =
    [
        new("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each", "قطعة"),
        new("PCS", UnitOfMeasureCategory.Count, 0, "pcs", "Pieces", "قطع"),
        new("CASE", UnitOfMeasureCategory.Count, 0, "case", "Case", "كرتونة"),
        new("PALLET", UnitOfMeasureCategory.Count, 0, "pallet", "Pallet", "طبالي"),
        new("KG", UnitOfMeasureCategory.Weight, 3, "kg", "Kilogram", "كيلوجرام"),
        new("G", UnitOfMeasureCategory.Weight, 3, "g", "Gram", "جرام"),
        new("LB", UnitOfMeasureCategory.Weight, 3, "lb", "Pound", "رطل"),
        new("M", UnitOfMeasureCategory.Length, 4, "m", "Meter", "متر"),
        new("CM", UnitOfMeasureCategory.Length, 2, "cm", "Centimeter", "سنتيمتر"),
        new("MM", UnitOfMeasureCategory.Length, 1, "mm", "Millimeter", "ملليمتر"),
        new("L", UnitOfMeasureCategory.Volume, 3, "L", "Liter", "لتر"),
        new("ML", UnitOfMeasureCategory.Volume, 3, "ml", "Milliliter", "ملليلتر")
    ];

    public async Task SeedAsync(
        WmsSeedProfile profile,
        CancellationToken cancellationToken = default)
    {
        await EnsureUnitOfMeasureCatalogAsync(cancellationToken);
        if (profile == WmsSeedProfile.None)
        {
            await EnsureGlobalSettingsAsync(null, cancellationToken);
            return;
        }

        var warehouse = await EnsureWarehouseAsync(cancellationToken);
        await EnsureGlobalSettingsAsync(warehouse.Id, cancellationToken);
        var locations = await EnsureLocationsAsync(warehouse, ReferenceLocations, cancellationToken);
        await EnsureWarehouseConfigurationAsync(warehouse, cancellationToken);

        if (profile == WmsSeedProfile.Reference)
        {
            return;
        }

        locations = await EnsureLocationsAsync(warehouse, DemoLocations, cancellationToken, locations);
        var items = await EnsureItemsAsync(cancellationToken);
        await EnsureStockAsync(items, locations, cancellationToken);
    }

    private async Task EnsureUnitOfMeasureCatalogAsync(CancellationToken cancellationToken)
    {
        var definitions = StandardUnits.ToDictionary(unit => unit.Code, StringComparer.Ordinal);
        var items = await context.Items
            .Include(item => item.Packagings)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        foreach (var code in items
                     .SelectMany(item => new[]
                     {
                         item.UnitOfMeasure,
                         item.PurchaseUnit,
                         item.SalesUnit
                     }.Concat(item.Packagings.Select(packaging => packaging.UnitOfMeasure)))
                     .Where(code => !string.IsNullOrWhiteSpace(code))
                     .Select(code => code.Trim().ToUpperInvariant())
                     .Distinct(StringComparer.Ordinal))
        {
            definitions.TryAdd(
                code,
                new SeedUnit(code, UnitOfMeasureCategory.Count, 4, code, code, code));
        }

        var existing = await context.UnitOfMeasures
            .ToDictionaryAsync(unit => unit.Code, StringComparer.Ordinal, cancellationToken);
        foreach (var definition in definitions.Values)
        {
            if (existing.ContainsKey(definition.Code))
            {
                continue;
            }

            context.UnitOfMeasures.Add(new UnitOfMeasure(
                definition.Code,
                definition.Category,
                definition.Precision,
                definition.Symbol,
                definition.Name,
                definition.LocalizedName));
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureGlobalSettingsAsync(
        int? defaultWarehouseId,
        CancellationToken cancellationToken)
    {
        var existing = await context.GlobalSettings
            .SingleOrDefaultAsync(
                settings => settings.Id == WmsGlobalSettingsEntity.GlobalId,
                cancellationToken);
        if (existing is not null)
        {
            return;
        }

        context.GlobalSettings.Add(WmsSettingsSeedFactory.Create(
            WmsSettingsDefaults.Create(),
            defaultWarehouseId,
            _clock.UtcNow));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Warehouse> EnsureWarehouseAsync(CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .SingleOrDefaultAsync(entity => entity.Code == "MAIN", cancellationToken);
        if (warehouse is not null)
        {
            return warehouse;
        }

        warehouse = new Warehouse("MAIN", "Main Warehouse");
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken);
        return warehouse;
    }

    private async Task EnsureWarehouseConfigurationAsync(
        Warehouse warehouse,
        CancellationToken cancellationToken)
    {
        var sequence = await context.WarehouseNumberSequences
            .SingleOrDefaultAsync(item => item.WarehouseId == warehouse.Id, cancellationToken);
        if (sequence is null)
        {
            context.WarehouseNumberSequences.Add(new WarehouseNumberSequence(warehouse.Id));
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<Dictionary<string, Location>> EnsureLocationsAsync(
        Warehouse warehouse,
        IReadOnlyList<SeedLocation> definitions,
        CancellationToken cancellationToken,
        Dictionary<string, Location>? existingLocations = null)
    {
        var locations = existingLocations ?? await context.Locations
            .ToDictionaryAsync(entity => entity.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var definition in definitions)
        {
            if (locations.TryGetValue(definition.Code, out var existing))
            {
                if (existing.WarehouseId != warehouse.Id)
                {
                    throw new InvalidOperationException(
                        $"Seed location '{definition.Code}' belongs to another warehouse.");
                }

                continue;
            }

            int? parentLocationId = null;
            if (definition.ParentCode is not null)
            {
                if (!locations.TryGetValue(definition.ParentCode, out var parent))
                {
                    throw new InvalidOperationException(
                        $"Seed location '{definition.Code}' requires missing parent '{definition.ParentCode}'.");
                }

                if (parent.WarehouseId != warehouse.Id)
                {
                    throw new InvalidOperationException(
                        $"Seed parent location '{definition.ParentCode}' belongs to another warehouse.");
                }

                parentLocationId = parent.Id;
            }

            var location = new Location(
                definition.Code,
                definition.Name,
                warehouse.Id,
                parentLocationId);
            location.SetPickable(definition.IsPickable);
            location.SetReceivable(definition.IsReceivable);
            context.Locations.Add(location);
            locations.Add(definition.Code, location);
            await context.SaveChangesAsync(cancellationToken);
        }

        return locations;
    }

    private async Task<Dictionary<string, Item>> EnsureItemsAsync(CancellationToken cancellationToken)
    {
        var items = await context.Items
            .Include(entity => entity.Barcodes)
            .ToDictionaryAsync(entity => entity.Sku, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var definition in DemoItems)
        {
            if (!items.TryGetValue(definition.Sku, out var item))
            {
                item = new Item(
                    definition.Sku,
                    definition.Name,
                    definition.UnitOfMeasure,
                    definition.RequiresLot,
                    definition.RequiresSerial);
                context.Items.Add(item);
                items.Add(definition.Sku, item);
            }
            else if (item.RequiresLot != definition.RequiresLot ||
                     item.RequiresSerial != definition.RequiresSerial)
            {
                throw new InvalidOperationException(
                    $"Existing item '{definition.Sku}' has tracking requirements that conflict with the Demo seed profile.");
            }

            if (!item.Name.Equals(definition.Name, StringComparison.Ordinal) ||
                !item.Description.Equals(definition.Description, StringComparison.Ordinal))
            {
                item.UpdateDetails(definition.Name, definition.Description);
            }

            if (item.ShelfLifeDays != definition.ShelfLifeDays)
            {
                item.SetShelfLife(definition.ShelfLifeDays);
            }
            foreach (var barcode in definition.Barcodes)
            {
                if (!item.Barcodes.Any(existing => existing.Value.Equals(barcode, StringComparison.OrdinalIgnoreCase)))
                {
                    item.AddBarcode(new Barcode(barcode));
                }
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return items;
    }

    private async Task EnsureStockAsync(
        Dictionary<string, Item> items,
        Dictionary<string, Location> locations,
        CancellationToken cancellationToken)
    {
        var existingStock = await context.Stock
            .Where(entity => entity.LotId == null &&
                             entity.SerialNumber == null &&
                             entity.InventoryStatusId == InventoryStatusSystemIds.Available)
            .ToDictionaryAsync(entity => (entity.ItemId, entity.LocationId), cancellationToken);

        foreach (var definition in DemoStock)
        {
            if (!items.TryGetValue(definition.Sku, out var item) ||
                !locations.TryGetValue(definition.LocationCode, out var location))
            {
                throw new InvalidOperationException(
                    $"Demo stock '{definition.Sku}/{definition.LocationCode}' references missing seed data.");
            }

            if (!existingStock.ContainsKey((item.Id, location.Id)))
            {
                context.Stock.Add(new Stock(
                    item.Id,
                    location.Id,
                    new Quantity(definition.Quantity)));
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed record SeedLocation(
        string Code,
        string Name,
        string? ParentCode,
        bool IsPickable,
        bool IsReceivable);

    private sealed record SeedItem(
        string Sku,
        string Name,
        string Description,
        string UnitOfMeasure,
        bool RequiresLot,
        bool RequiresSerial,
        int ShelfLifeDays,
        IReadOnlyList<string> Barcodes);

    private sealed record SeedStock(string Sku, string LocationCode, decimal Quantity);

    private sealed record SeedUnit(
        string Code,
        UnitOfMeasureCategory Category,
        int Precision,
        string Symbol,
        string Name,
        string LocalizedName);
}
