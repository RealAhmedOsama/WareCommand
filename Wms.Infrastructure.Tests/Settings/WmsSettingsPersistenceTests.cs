using Wms.Domain.Entities;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Settings;

namespace Wms.Infrastructure.Tests.Settings;

public sealed class WmsSettingsPersistenceTests : IDisposable
{
    private readonly WmsDbContext _context;

    public WmsSettingsPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new WmsDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task GlobalAndWarehouseOverrideRowsPersistAsSeparateExplicitScopes()
    {
        var warehouse = new Warehouse("SETTINGS", "Settings Warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();

        _context.GlobalSettings.Add(new WmsGlobalSettingsEntity
        {
            CompanyName = "WareCommand",
            CompanyCode = "WARECOMMAND",
            DefaultReceivingLocationCode = "RECEIVE",
            ReceivingPrefix = "RCV-",
            NextReceivingNumber = 1,
            ShippingPrefix = "SHP-",
            NextShippingNumber = 1,
            AdjustmentPrefix = "ADJ-",
            NextAdjustmentNumber = 1,
            LowStockThreshold = 10,
            LowStockAlertLimit = 10,
            RecentMovementLimit = 10,
            DashboardRefreshIntervalSeconds = 300,
            RequireLocationForAdjustment = true,
            MaximumAdjustmentQuantity = 1_000_000,
            ExpiryWarningDays = 30,
            BlockExpiredReceipt = true,
            ScannerTimeoutMilliseconds = 5_000,
            MinimumBarcodeLength = 3,
            MaximumBarcodeLength = 50,
            LabelTemplateName = "default",
            LabelPaperSize = "A4",
            IncludeCompanyNameOnLabels = true,
            DefaultReportPeriodDays = 7,
            MaximumReportRows = 10_000,
            DefaultLocale = "en-US",
            DefaultTimeZone = "UTC",
            CurrencyCode = "USD",
            IntegrationTimeoutSeconds = 30,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        _context.WarehouseSettingsOverrides.Add(new WmsWarehouseSettingsOverrideEntity
        {
            WarehouseId = warehouse.Id,
            LowStockThreshold = 4,
            DashboardRefreshIntervalSeconds = 60,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        await _context.SaveChangesAsync();

        (await _context.GlobalSettings.SingleAsync()).LowStockThreshold.Should().Be(10);
        var warehouseOverride = await _context.WarehouseSettingsOverrides.SingleAsync();
        warehouseOverride.WarehouseId.Should().Be(warehouse.Id);
        warehouseOverride.LowStockThreshold.Should().Be(4);
        warehouseOverride.DashboardRefreshIntervalSeconds.Should().Be(60);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
