using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Suppliers;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Suppliers;

namespace Wms.Infrastructure.Tests.Suppliers;

public sealed class SupplierManagementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly WmsDbContext _context;
    private readonly SupplierManagementService _service;

    public SupplierManagementServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new SupplierManagementService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<SupplierManagementService>.Instance);
    }

    [Fact]
    public async Task CreateAndListAsync_PersistsReceivingDefaultsAndItemReference()
    {
        var warehouse = new Warehouse("SUP-WH", "Supplier Warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        var dock = new Location(
            "SUP-DOCK",
            "Supplier Dock",
            warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        var item = new Item("SUP-ITEM", "Supplier Widget", "EA");
        _context.AddRange(dock, item);
        await _context.SaveChangesAsync();

        var result = await _service.CreateAsync(
            new SupplierInput(
                "acme",
                "Acme Distribution",
                LocalizedName: "أكمي",
                ExternalErpIdentifier: "erp-001",
                ContactEmail: "receiving@acme.example",
                ReceivingDefaults: new SupplierReceivingDefaultsInput(
                    warehouse.Id,
                    dock.Id,
                    DefaultLeadTimeDays: 5,
                    OverDeliveryTolerancePercent: 2,
                    UnderDeliveryTolerancePercent: 1,
                    RequiresLot: true,
                    RequiresExpiry: true,
                    DefaultCurrencyCode: "usd"),
                ItemReferences: [
                    new SupplierItemReferenceInput(
                        null,
                        item.Id,
                        "vendor-widget",
                        VendorBarcode: "vendor-barcode",
                        MinimumOrderQuantity: 10)]),
            "user-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        var persistedSupplier = await _context.Suppliers.SingleAsync();
        persistedSupplier.PreferredWarehouseId.Should().Be(warehouse.Id);
        (await _context.Warehouses.SingleAsync()).Code.Should().Be("SUP-WH");
        result.Value.Code.Should().Be("ACME");
        result.Value.PreferredWarehouseCode.Should().Be("SUP-WH");
        result.Value.PreferredDockCode.Should().Be("SUP-DOCK");
        result.Value.DefaultCurrencyCode.Should().Be("USD");
        result.Value.ItemReferences.Should().ContainSingle(reference =>
            reference.ItemSku == "SUP-ITEM" && reference.VendorSku == "VENDOR-WIDGET");
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.SupplierCreated),
            It.IsAny<CancellationToken>()),
            Times.Once);

        var page = await _service.ListAsync(new SupplierListQuery(SearchTerm: "vendor-widget"));
        page.IsSuccess.Should().BeTrue(page.Error);
        page.Value.TotalCount.Should().Be(1);
        page.Value.Suppliers[0].Code.Should().Be("ACME");
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateCodeAndExternalErpIdentifier()
    {
        var first = await _service.CreateAsync(
            new SupplierInput("SUP-001", "First Supplier", ExternalErpIdentifier: "erp-1"),
            "user-1");
        var duplicateCode = await _service.CreateAsync(
            new SupplierInput("sup-001", "Other Supplier"),
            "user-1");
        var duplicateExternal = await _service.CreateAsync(
            new SupplierInput("SUP-002", "Other Supplier", ExternalErpIdentifier: "ERP-1"),
            "user-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        duplicateCode.ErrorCode.Should().Be("supplier.code_conflict");
        duplicateExternal.ErrorCode.Should().Be("supplier.external_erp_conflict");
    }

    [Fact]
    public async Task LifecycleAndResolution_KeepInactiveSupplierOutOfNewDocumentLookups()
    {
        var item = new Item("SUP-RESOLVE", "Resolvable Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var created = await _service.CreateAsync(
            new SupplierInput(
                "SUP-RES",
                "Resolvable Supplier",
                ItemReferences: [new SupplierItemReferenceInput(null, item.Id, "VENDOR-RES")]),
            "user-1");

        var deactivated = await _service.SetActiveAsync(created.Value.Id, false, "user-1");
        var blocked = await _service.ResolveItemReferenceAsync(
            new SupplierItemReferenceResolutionQuery(created.Value.Id, VendorSku: "vendor-res"));
        var historical = await _service.ResolveItemReferenceAsync(
            new SupplierItemReferenceResolutionQuery(
                created.Value.Id,
                VendorSku: "vendor-res",
                IncludeInactive: true));

        deactivated.IsSuccess.Should().BeTrue(deactivated.Error);
        blocked.ErrorCode.Should().Be("supplier.inactive");
        historical.IsSuccess.Should().BeTrue(historical.Error);
        historical.Value.ItemSku.Should().Be("SUP-RESOLVE");

        var reactivated = await _service.SetActiveAsync(created.Value.Id, true, "user-1");
        reactivated.IsSuccess.Should().BeTrue(reactivated.Error);
        (await _service.ResolveItemReferenceAsync(
            new SupplierItemReferenceResolutionQuery(created.Value.Id, VendorSku: "VENDOR-RES")))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_BlocksSupplierItemMappings()
    {
        var item = new Item("SUP-DELETE", "Referenced Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var created = await _service.CreateAsync(
            new SupplierInput(
                "SUP-REF",
                "Referenced Supplier",
                ItemReferences: [new SupplierItemReferenceInput(null, item.Id, "VENDOR-REF")]),
            "user-1");

        var deleted = await _service.DeleteAsync(created.Value.Id, "user-1");

        deleted.ErrorCode.Should().Be("supplier.referenced");
        (await _context.Suppliers.AnyAsync(supplier => supplier.Id == created.Value.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_PreservesDefaultsWhenOmittedAndReassignsItemReference()
    {
        var warehouse = new Warehouse("SUP-UPD-WH", "Update Warehouse");
        var firstItem = new Item("SUP-UPD-1", "First Widget", "EA");
        var secondItem = new Item("SUP-UPD-2", "Second Widget", "EA");
        _context.AddRange(warehouse, firstItem, secondItem);
        await _context.SaveChangesAsync();
        var created = await _service.CreateAsync(
            new SupplierInput(
                "SUP-UPD",
                "Original Supplier",
                ReceivingDefaults: new SupplierReceivingDefaultsInput(warehouse.Id, DefaultLeadTimeDays: 7),
                ItemReferences: [new SupplierItemReferenceInput(null, firstItem.Id, "VENDOR-OLD")]),
            "user-1");

        var updated = await _service.UpdateAsync(
            created.Value.Id,
            new SupplierInput(
                "SUP-UPD",
                "Updated Supplier",
                ItemReferences: [new SupplierItemReferenceInput(
                    created.Value.ItemReferences[0].Id,
                    secondItem.Id,
                    "VENDOR-NEW")]),
            "user-1");

        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value.PreferredWarehouseCode.Should().Be("SUP-UPD-WH");
        updated.Value.DefaultLeadTimeDays.Should().Be(7);
        updated.Value.ItemReferences.Should().ContainSingle(reference =>
            reference.ItemSku == "SUP-UPD-2" && reference.VendorSku == "VENDOR-NEW");
    }

    [Fact]
    public async Task ImportAsync_ResolvesWarehouseAndItemReferenceAndRejectsBadNumbers()
    {
        var warehouse = new Warehouse("SUP-IMP-WH", "Import Warehouse");
        var item = new Item("SUP-IMP-ITEM", "Imported Widget", "EA");
        _context.AddRange(warehouse, item);
        await _context.SaveChangesAsync();

        var header = "CODE,LEGAL_NAME,LOCALIZED_NAME,TAX_REGISTRATION_NUMBER,EXTERNAL_ERP_IDENTIFIER,ADDRESS_LINE_1,ADDRESS_LINE_2,CITY,STATE_OR_PROVINCE,POSTAL_CODE,COUNTRY_CODE,CONTACT_NAME,CONTACT_EMAIL,CONTACT_PHONE,IS_ACTIVE,PREFERRED_WAREHOUSE_CODE,PREFERRED_DOCK_CODE,DEFAULT_LEAD_TIME_DAYS,OVER_DELIVERY_TOLERANCE_PERCENT,UNDER_DELIVERY_TOLERANCE_PERCENT,REQUIRES_LOT,REQUIRES_EXPIRY,QUALITY_PROFILE,LABEL_RULE,DEFAULT_CURRENCY_CODE,NOTES,ITEM_SKU,VENDOR_SKU,VENDOR_BARCODE,ITEM_PACKAGING_CODE,VENDOR_PACKAGING,UNITS_PER_PURCHASE_PACKAGE,MINIMUM_ORDER_QUANTITY,REFERENCE_LEAD_TIME_DAYS,REFERENCE_ACTIVE";
        var row = string.Join(",", [
            "IMP-001", "Imported Supplier", "", "", "", "", "", "", "", "", "EG", "", "", "",
            "true", "SUP-IMP-WH", "", "4", "2", "1", "true", "false", "STANDARD", "BOX", "USD", "",
            "SUP-IMP-ITEM", "VENDOR-IMP", "VB-IMP", "", "", "12", "5", "3", "true"]);
        var imported = await _service.ImportAsync($"{header}\n{row}", "user-1");

        imported.IsSuccess.Should().BeTrue(imported.Error);
        imported.Value.ImportedSupplierCount.Should().Be(1);
        imported.Value.ImportedReferenceCount.Should().Be(1);
        var saved = await _context.Suppliers
            .Include(supplier => supplier.ItemReferences)
            .SingleAsync(supplier => supplier.Code == "IMP-001");
        saved.DefaultCurrencyCode.Should().Be("USD");
        saved.ItemReferences.Should().ContainSingle(reference => reference.VendorSku == "VENDOR-IMP");

        var invalidRow = string.Join(",", [
            "BAD-001", "Bad Supplier", "", "", "", "", "", "", "", "", "", "", "", "",
            "true", "", "", "not-a-number", "", "", "", "", "", "", "", ""]);
        var invalid = await _service.ImportAsync($"{header}\n{invalidRow}", "user-1");
        invalid.ErrorCode.Should().Be("supplier.import_invalid");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
