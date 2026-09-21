using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryInquiryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly WmsDbContext _context;
    private readonly InventoryInquiryService _service;
    private readonly Warehouse _warehouse;
    private readonly Warehouse _otherWarehouse;
    private readonly Location _location;
    private readonly Location _otherWarehouseLocation;
    private readonly Item _item;
    private readonly Lot _lot;
    private readonly InventoryStatus _availableStatus;
    private readonly InventoryStatus _holdStatus;

    public InventoryInquiryServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));

        _service = new InventoryInquiryService(
            _context,
            _warehouseAccess.Object,
            NullLogger<InventoryInquiryService>.Instance);

        _warehouse = new Warehouse("INQ-WH", "Inquiry Warehouse");
        _otherWarehouse = new Warehouse("INQ-WH-2", "Other Inquiry Warehouse");
        _item = new Item("INQ-ITEM", "Inquiry Widget", "EA");
        _item.AddBarcode(new Barcode("987654321012"));
        _availableStatus = new InventoryStatus(
            "INQ_AVAILABLE",
            "Inquiry available",
            "متاح للاستعلام",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _holdStatus = new InventoryStatus(
            InventoryStatusCodes.Hold,
            "Hold",
            "تعليق",
            isAvailable: false,
            isAllocatable: false,
            isPickable: false,
            isShippable: false,
            isCountable: true);

        _context.AddRange(_warehouse, _otherWarehouse, _item, _availableStatus, _holdStatus);
        _context.SaveChanges();

        _location = new Location("INQ-A", "Inquiry Pick Face", _warehouse.Id);
        _otherWarehouseLocation = new Location("INQ-B", "Other Warehouse Face", _otherWarehouse.Id);
        _context.AddRange(_location, _otherWarehouseLocation);
        _context.SaveChanges();

        _lot = new Lot(
            "LOT-INQ",
            _item.Id,
            DateTime.UtcNow.Date.AddDays(30));
        _context.Add(_lot);
        _context.SaveChanges();

        var availableBalance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            _lot.Id,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        availableBalance.Apply(10m, 2m, allowNegativeStock: false);

        var heldBalance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _holdStatus.Id,
            _item.UnitOfMeasure));
        heldBalance.Apply(4m, 0m, allowNegativeStock: false);

        var otherWarehouseBalance = new InventoryBalance(new InventoryBalanceKey(
            _otherWarehouse.Id,
            _otherWarehouseLocation.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        otherWarehouseBalance.Apply(7m, 1m, allowNegativeStock: false);

        _context.AddRange(availableBalance, heldBalance, otherWarehouseBalance);
        _context.SaveChanges();
    }

    [Fact]
    public async Task QueryAppliesCombinedFiltersBeforePagingAndProjectsExplicitQuantities()
    {
        var result = await _service.QueryAsync(new InventoryInquiryQuery(
            WarehouseId: _warehouse.Id,
            LocationId: _location.Id,
            ItemId: _item.Id,
            LotId: _lot.Id,
            ScanValue: " lot-inq ",
            AvailableOnly: true,
            Page: 1,
            PageSize: 1));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.TotalCount.Should().Be(1);
        result.Value.TotalPages.Should().Be(1);
        result.Value.Items.Should().ContainSingle();

        var item = result.Value.Items[0];
        item.ItemSku.Should().Be("INQ-ITEM");
        item.QuantityAvailable.Should().Be(10m);
        item.QuantityReserved.Should().Be(2m);
        item.AvailableQuantity.Should().Be(8m);
        item.AvailableToPromiseQuantity.Should().Be(8m);
        item.HeldQuantity.Should().Be(0m);
        item.InTransitQuantity.Should().Be(0m);
        item.OrderedQuantity.Should().Be(0m);
        item.ExpiryDate.Should().Be(_lot.ExpiryDate);
    }

    [Fact]
    public async Task SearchNormalizesInputAndMatchesItemNameAndBarcodeFields()
    {
        var nameResult = await _service.QueryAsync(new InventoryInquiryQuery(
            SearchTerm: "  inquiry widget  ",
            PageSize: 10));
        var barcodeResult = await _service.QueryAsync(new InventoryInquiryQuery(
            ScanValue: " 987654321012 ",
            PageSize: 10));

        nameResult.IsSuccess.Should().BeTrue(nameResult.Error);
        nameResult.Value.TotalCount.Should().Be(3);
        barcodeResult.IsSuccess.Should().BeTrue();
        barcodeResult.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task QueryUsesDeterministicQuantityOrderingAndPageMetadata()
    {
        var firstPage = await _service.QueryAsync(new InventoryInquiryQuery(
            IncludeZero: true,
            Sort: InventoryInquirySort.Quantity,
            Descending: true,
            Page: 1,
            PageSize: 1));
        var secondPage = await _service.QueryAsync(new InventoryInquiryQuery(
            IncludeZero: true,
            Sort: InventoryInquirySort.Quantity,
            Descending: true,
            Page: 2,
            PageSize: 1));

        firstPage.IsSuccess.Should().BeTrue();
        secondPage.IsSuccess.Should().BeTrue();
        firstPage.Value.TotalCount.Should().Be(3);
        firstPage.Value.TotalPages.Should().Be(3);
        firstPage.Value.Items.Single().QuantityAvailable.Should().Be(10m);
        secondPage.Value.Items.Single().QuantityAvailable.Should().Be(7m);
    }

    [Fact]
    public async Task SummaryAggregatesOnServerReadModelByItemAndStatus()
    {
        var result = await _service.SummarizeAsync(new InventoryInquiryQuery(
            WarehouseId: _warehouse.Id));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(summary =>
            summary.InventoryStatusCode == "INQ_AVAILABLE" &&
            summary.TotalQuantity == 10m &&
            summary.TotalReserved == 2m &&
            summary.TotalAvailable == 8m &&
            summary.LocationCount == 1);
        result.Value.Should().Contain(summary =>
            summary.InventoryStatusCode == InventoryStatusCodes.Hold &&
            summary.TotalQuantity == 4m &&
            summary.TotalAvailable == 4m);
    }

    [Fact]
    public async Task DashboardMetricsAggregateCanonicalBalancesAndExpiryWindow()
    {
        var result = await _service.GetDashboardMetricsAsync(
            new InventoryInquiryQuery(WarehouseId: _warehouse.Id),
            DateOnly.FromDateTime(DateTime.UtcNow),
            expiryWarningDays: 30);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.StockKeepingUnits.Should().Be(1);
        result.Value.OnHandQuantity.Should().Be(14m);
        result.Value.ReservedQuantity.Should().Be(2m);
        result.Value.AvailableQuantity.Should().Be(12m);
        result.Value.HeldQuantity.Should().Be(4m);
        result.Value.DamagedQuantity.Should().Be(0m);
        result.Value.ExpiredQuantity.Should().Be(0m);
        result.Value.ExpiringQuantity.Should().Be(10m);
        result.Value.StockLocations.Should().Be(1);
    }

    [Fact]
    public async Task LimitedWarehouseScopeCannotReadAnotherWarehouse()
    {
        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(
                false,
                new HashSet<int> { _warehouse.Id }));

        var result = await _service.QueryAsync(new InventoryInquiryQuery(
            WarehouseId: _otherWarehouse.Id));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyStockFallbackRemainsServerPagedAndAggregatedDuringCutover()
    {
        _context.InventoryBalances.RemoveRange(await _context.InventoryBalances.ToListAsync());
        _context.Stock.AddRange(
            new Stock(
                _item.Id,
                _location.Id,
                new Quantity(6m),
                inventoryStatusId: _availableStatus.Id),
            new Stock(
                _item.Id,
                _otherWarehouseLocation.Id,
                new Quantity(8m),
                inventoryStatusId: _availableStatus.Id));
        await _context.SaveChangesAsync();

        var page = await _service.QueryAsync(new InventoryInquiryQuery(
            WarehouseId: _warehouse.Id,
            AvailableOnly: true,
            PageSize: 1));
        var summary = await _service.SummarizeAsync(new InventoryInquiryQuery(
            WarehouseId: _warehouse.Id));
        var metrics = await _service.GetDashboardMetricsAsync(
            new InventoryInquiryQuery(WarehouseId: _warehouse.Id),
            DateOnly.FromDateTime(DateTime.UtcNow),
            expiryWarningDays: 30);

        page.IsSuccess.Should().BeTrue(page.Error);
        page.Value.TotalCount.Should().Be(1);
        page.Value.Items.Single().QuantityAvailable.Should().Be(6m);
        summary.IsSuccess.Should().BeTrue(summary.Error);
        summary.Value.Should().ContainSingle();
        summary.Value.Single().TotalQuantity.Should().Be(6m);
        metrics.IsSuccess.Should().BeTrue(metrics.Error);
        metrics.Value.OnHandQuantity.Should().Be(6m);
        metrics.Value.AvailableQuantity.Should().Be(6m);
        metrics.Value.StockLocations.Should().Be(1);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
