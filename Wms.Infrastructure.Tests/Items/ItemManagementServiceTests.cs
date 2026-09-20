using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Items;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Items;

namespace Wms.Infrastructure.Tests.Items;

public sealed class ItemManagementServiceTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private WmsDbContext _context = null!;
    private ItemManagementService _service = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _context.Database.EnsureCreatedAsync();

        _access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new ItemManagementService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<ItemManagementService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_PersistsMasterFieldsAndPackaging()
    {
        var result = await _service.CreateAsync(
            CreateRequest("ITEM-001", "Widget", "123456", category: "Hardware"),
            "user-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Category.Should().Be("Hardware");
        result.Value.Packagings.Should().ContainSingle();
        result.Value.Packagings[0].Barcode.Should().Be("9123456");

        var saved = await _context.Items
            .Include(item => item.Packagings)
            .SingleAsync();
        saved.PurchaseUnit.Should().Be("BOX");
        saved.RequiresExpiry.Should().BeTrue();
        saved.Packagings.Should().ContainSingle();
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.ItemCreated),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateSkuAndGlobalBarcodeReuse()
    {
        (await _service.CreateAsync(CreateRequest("ITEM-001", "Widget", "123456"), "user-1"))
            .IsSuccess.Should().BeTrue();

        var duplicateSku = await _service.CreateAsync(CreateRequest("item-001", "Other", "222222"), "user-1");
        var duplicateBarcode = await _service.CreateAsync(CreateRequest("ITEM-002", "Other", "123456"), "user-1");
        var packageBarcodeConflict = await _service.CreateAsync(
            CreateRequest("ITEM-003", "Other", "333333", packagingBarcode: "9123456"),
            "user-1");

        duplicateSku.ErrorCode.Should().Be("item.sku_conflict");
        duplicateBarcode.ErrorCode.Should().Be("item.barcode_conflict");
        packageBarcodeConflict.ErrorCode.Should().Be("item.barcode_conflict");
    }

    [Fact]
    public async Task ListAsync_ProvidesStablePagingAndInactiveFiltering()
    {
        for (var index = 1; index <= 3; index++)
        {
            var created = await _service.CreateAsync(
                CreateRequest($"ITEM-{index:000}", $"Widget {index}", $"{index:000000}"),
                "user-1");
            created.IsSuccess.Should().BeTrue();
            if (index == 2)
            {
                (await _service.SetActiveAsync(created.Value.Id, false, "user-1")).IsSuccess.Should().BeTrue();
            }
        }

        var activePage = await _service.ListAsync(new ItemListQuery(Page: 1, PageSize: 1));
        var allPage = await _service.ListAsync(new ItemListQuery(IncludeInactive: true, Page: 2, PageSize: 2));

        activePage.Value.TotalCount.Should().Be(2);
        activePage.Value.Items.Should().ContainSingle();
        allPage.Value.TotalCount.Should().Be(3);
        allPage.Value.Items.Should().ContainSingle();
        allPage.Value.Items[0].Sku.Should().Be("ITEM-003");
    }

    [Fact]
    public async Task UpdateAsync_BlocksTrackingPolicyChangesAfterStockHistory()
    {
        var created = await _service.CreateAsync(CreateRequest("ITEM-001", "Widget", "123456"), "user-1");
        var warehouse = new Warehouse("MAIN", "Main");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        var location = new Location("BIN-01", "Bin", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        _context.Stock.Add(new Stock(created.Value.Id, location.Id, new Quantity(10)));
        await _context.SaveChangesAsync();

        var result = await _service.UpdateAsync(
            UpdateRequest(created.Value.Id, requiresLot: true),
            "user-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("item.tracking_policy_locked");
    }

    [Fact]
    public async Task DeleteAsync_BlocksHistoryButDeletesUnreferencedItem()
    {
        var withHistory = await _service.CreateAsync(CreateRequest("ITEM-001", "Widget", "123456"), "user-1");
        var warehouse = new Warehouse("MAIN", "Main");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        var location = new Location("BIN-01", "Bin", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        _context.Stock.Add(new Stock(withHistory.Value.Id, location.Id, new Quantity(1)));
        await _context.SaveChangesAsync();

        var blocked = await _service.DeleteAsync(withHistory.Value.Id, "user-1");
        blocked.ErrorCode.Should().Be("item.history_exists");

        var unreferenced = await _service.CreateAsync(CreateRequest("ITEM-002", "Other", "222222"), "user-1");
        var deleted = await _service.DeleteAsync(unreferenced.Value.Id, "user-1");

        deleted.IsSuccess.Should().BeTrue();
        (await _context.Items.AnyAsync(item => item.Id == unreferenced.Value.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateAsync_CopiesMasterAndPackagingWithoutReusingBarcodes()
    {
        var source = await _service.CreateAsync(CreateRequest("ITEM-001", "Widget", "123456"), "user-1");

        var duplicate = await _service.DuplicateAsync(
            new ItemDuplicateRequest(source.Value.Id, "ITEM-002"),
            "user-1");

        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Value.LifecycleStatus.Should().Be(ItemLifecycleStatus.Draft);
        duplicate.Value.Barcodes.Should().BeEmpty();
        duplicate.Value.Packagings.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportAndExportAsync_UseBoundedCsvContract()
    {
        var csv = "SKU,NAME,BASE_UNIT,CATEGORY,BRAND,TYPE,STATUS,PURCHASE_UNIT,SALES_UNIT,REQUIRES_LOT,REQUIRES_SERIAL,REQUIRES_EXPIRY,SHELF_LIFE_DAYS,USE_FEFO,STANDARD_COST,MINIMUM_STOCK,MAXIMUM_STOCK,SAFETY_STOCK,LEAD_TIME_DAYS,STORAGE_PROFILE,PUTAWAY_PROFILE,DEFAULT_SUPPLIER_CODE\n" +
                  "IMPORT-001,Imported,EA,Hardware,Acme,Stock,Active,BOX,EA,false,false,false,0,false,2.5,1,10,2,3,Standard,Fast,SUP-1";

        var imported = await _service.ImportAsync(csv, "user-1");
        var exported = await _service.ExportAsync(new ItemListQuery(SearchTerm: "IMPORT-001", IncludeInactive: true));

        imported.IsSuccess.Should().BeTrue();
        imported.Value.ImportedCount.Should().Be(1);
        exported.IsSuccess.Should().BeTrue();
        exported.Value.Should().Contain("IMPORT-001");
        exported.Value.Should().Contain("Standard");
    }

    [Fact]
    public async Task CreateAsync_PersistsNestedPackagingRolesAndRejectsGlobalGtinReuse()
    {
        var created = await _service.CreateAsync(
            new ItemCreateRequest(
                "PACK-001",
                new ItemCommercialRequest("Packaged item", LocalizedName: "منتج معبأ"),
                new ItemMeasurementRequest("EA"),
                new ItemTrackingRequest(),
                new ItemStorageRequest(),
                new ItemPlanningRequest(),
                Packagings:
                [
                    new ItemPackagingRequest(
                        "CASE",
                        "EA",
                        12m,
                        Gtin: "0001234567890",
                        Type: PackagingType.Case,
                        IsDefaultStorage: true),
                    new ItemPackagingRequest(
                        "PALLET",
                        "CASE",
                        10m,
                        Gtin: "0001234567891",
                        ParentPackagingCode: "CASE",
                        Type: PackagingType.Pallet,
                        IsDefaultShipping: true)
                ]),
            "user-1");

        created.IsSuccess.Should().BeTrue();
        created.Value.Packagings.Should().HaveCount(2);
        created.Value.Packagings.Single(value => value.Code == "PALLET")
            .ParentPackagingCode.Should().Be("CASE");
        created.Value.Packagings.Single(value => value.Code == "PALLET")
            .IsDefaultShipping.Should().BeTrue();

        var duplicateGtin = await _service.CreateAsync(
            new ItemCreateRequest(
                "PACK-002",
                new ItemCommercialRequest("Other packaged item"),
                new ItemMeasurementRequest("EA"),
                new ItemTrackingRequest(),
                new ItemStorageRequest(),
                new ItemPlanningRequest(),
                Packagings:
                [new ItemPackagingRequest("CASE", "EA", 12m, Gtin: "0001234567890")]),
            "user-1");

        duplicateGtin.ErrorCode.Should().Be("item.barcode_conflict");
    }

    [Fact]
    public async Task UpdateAsync_VersionsExistingPackagingWithoutRewritingItsIdentity()
    {
        var created = await _service.CreateAsync(
            CreateRequest("PACK-003", "Versioned", "123456"),
            "user-1");

        var updated = await _service.UpdateAsync(
            new ItemUpdateRequest(
                created.Value.Id,
                new ItemCommercialRequest("Versioned"),
                new ItemMeasurementRequest("EA", "BOX", "EA"),
                new ItemTrackingRequest(RequiresExpiry: true, ShelfLifeDays: 30, UseFefo: true),
                new ItemStorageRequest(StorageProfile: "Standard"),
                new ItemPlanningRequest(ItemReorderPolicy.MinMax, 1, 100, 5, 7),
                ["123456"],
                [new ItemPackagingRequest("CASE", "EA", 24m, "654321", IsDefault: true)]),
            "user-1");

        updated.IsSuccess.Should().BeTrue();
        var saved = await _context.ItemPackagings.SingleAsync();
        saved.UnitsPerPackage.Should().Be(24m);
        saved.Version.Should().Be(2);
    }

    [Fact]
    public async Task ExportAsync_IncludesPackagingMasterDataAsBoundedJson()
    {
        await _service.CreateAsync(
            new ItemCreateRequest(
                "PACK-EXPORT",
                new ItemCommercialRequest("Exported package"),
                new ItemMeasurementRequest("EA"),
                new ItemTrackingRequest(),
                new ItemStorageRequest(),
                new ItemPlanningRequest(),
                Packagings:
                [new ItemPackagingRequest(
                    "CASE",
                    "EA",
                    12m,
                    Name: "Case",
                    LocalizedName: "علبة",
                    Type: PackagingType.Case)]),
            "user-1");

        var exported = await _service.ExportAsync(
            new ItemListQuery(SearchTerm: "PACK-EXPORT", IncludeInactive: true));

        exported.IsSuccess.Should().BeTrue();
        exported.Value.Should().Contain("PACKAGINGS_JSON");
        exported.Value.Should().Contain("CASE");
        exported.Value.Should().Contain("علبة");
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ItemCreateRequest CreateRequest(
        string sku,
        string name,
        string barcode,
        string? category = null,
        string? packagingBarcode = null) =>
        CreateRequestCore(sku, name, barcode, category, packagingBarcode ?? "9" + barcode);

    private static ItemCreateRequest CreateRequestCore(
        string sku,
        string name,
        string barcode,
        string? category,
        string packagingBarcode) =>
        new(
            sku,
            new ItemCommercialRequest(name, Category: category),
            new ItemMeasurementRequest("EA", "BOX", "EA"),
            new ItemTrackingRequest(RequiresExpiry: true, ShelfLifeDays: 30, UseFefo: true),
            new ItemStorageRequest(StorageProfile: "Standard"),
            new ItemPlanningRequest(ItemReorderPolicy.MinMax, 1, 100, 5, 7),
            [barcode],
            [new ItemPackagingRequest("CASE", "EA", 12, packagingBarcode, IsDefault: true)]);

    private static ItemUpdateRequest UpdateRequest(int id, bool requiresLot) =>
        new(
            id,
            new ItemCommercialRequest("Updated"),
            new ItemMeasurementRequest("EA", "BOX", "EA"),
            new ItemTrackingRequest(RequiresLot: requiresLot, RequiresExpiry: true, ShelfLifeDays: 30, UseFefo: true),
            new ItemStorageRequest(StorageProfile: "Standard"),
            new ItemPlanningRequest(ItemReorderPolicy.MinMax, 1, 100, 5, 7),
            ["123456"],
            [new ItemPackagingRequest("CASE", "EA", 12, "654321", IsDefault: true)]);
}
