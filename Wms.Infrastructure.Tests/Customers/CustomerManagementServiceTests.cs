using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Customers;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Infrastructure.Customers;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Tests.Customers;

public sealed class CustomerManagementServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly WmsDbContext _context;
    private readonly CustomerManagementService _service;

    public CustomerManagementServiceTests()
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
        _audit
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new CustomerManagementService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<CustomerManagementService>.Instance);
    }

    [Fact]
    public async Task CreateListAndResolveAsync_PersistsDefaultsAndReferences()
    {
        var item = new Item("CUST-ITEM", "Customer Item", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var created = await _service.CreateAsync(
            new CustomerInput(
                " acme ",
                "Acme Retail",
                LocalizedName: "أكمي",
                ExternalErpIdentifier: "erp-1",
                DefaultCarrierCode: "carrier",
                DefaultCarrierServiceCode: "next-day",
                AllowPartialShipment: true,
                ShipToAddresses: [new CustomerShipToAddressInput(
                    null,
                    "main",
                    "Main Recipient",
                    Phone: "+201000000000",
                    CountryCode: "eg",
                    City: "Cairo",
                    AddressLine1: "First line",
                    IsDefault: true)],
                ItemReferences: [new CustomerItemReferenceInput(
                    null,
                    item.Id,
                    "customer-sku")]),
            "user-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.Code.Should().Be("ACME");
        created.Value.DefaultCarrierServiceCode.Should().Be("NEXT-DAY");
        created.Value.ShipToAddresses.Should().ContainSingle(address => address.Code == "MAIN" && address.IsDefault);
        created.Value.ItemReferences.Should().ContainSingle(reference => reference.CustomerSku == "CUSTOMER-SKU");

        var snapshot = await _service.GetDocumentSnapshotAsync(new CustomerDocumentSnapshotQuery(created.Value.Id));
        snapshot.IsSuccess.Should().BeTrue(snapshot.Error);
        snapshot.Value.ShipToCode.Should().Be("MAIN");
        snapshot.Value.AllowPartialShipment.Should().BeTrue();

        var resolved = await _service.ResolveItemReferenceAsync(
            new CustomerItemReferenceResolutionQuery(created.Value.Id, CustomerSku: "customer-sku"));
        resolved.IsSuccess.Should().BeTrue(resolved.Error);
        resolved.Value.ItemSku.Should().Be("CUST-ITEM");

        var page = await _service.ListAsync(new CustomerListQuery(SearchTerm: "main"));
        page.IsSuccess.Should().BeTrue(page.Error);
        page.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task DuplicateIdentifiersAndInactiveSelectionAreRejected()
    {
        var first = await _service.CreateAsync(
            new CustomerInput("CUST-001", "First", ExternalChannelIdentifier: "channel-1"),
            "user-1");
        var duplicateCode = await _service.CreateAsync(
            new CustomerInput("cust-001", "Other"),
            "user-1");
        var duplicateChannel = await _service.CreateAsync(
            new CustomerInput("CUST-002", "Other", ExternalChannelIdentifier: "CHANNEL-1"),
            "user-1");

        await _service.SetActiveAsync(first.Value.Id, false, "user-1");
        var snapshot = await _service.GetDocumentSnapshotAsync(new CustomerDocumentSnapshotQuery(first.Value.Id));

        duplicateCode.ErrorCode.Should().Be("customer.code_conflict");
        duplicateChannel.ErrorCode.Should().Be("customer.external_channel_conflict");
        snapshot.ErrorCode.Should().Be("customer.inactive");
    }

    [Fact]
    public async Task UpdateDeactivatesOmittedShipToWithoutChangingHistoricalIdentifier()
    {
        var created = await _service.CreateAsync(
            new CustomerInput(
                "CUST-UPD",
                "Original",
                ShipToAddresses: [new CustomerShipToAddressInput(
                    null,
                    "main",
                    "Main",
                    CountryCode: "EG",
                    AddressLine1: "One")]),
            "user-1");

        var updated = await _service.UpdateAsync(
            created.Value.Id,
            new CustomerInput(
                "CUST-UPD",
                "Updated",
                ShipToAddresses: [new CustomerShipToAddressInput(
                    created.Value.ShipToAddresses[0].Id,
                    "main",
                    "Updated Main",
                    CountryCode: "EG",
                    AddressLine1: "Two")]),
            "user-1");

        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value.LegalName.Should().Be("Updated");
        updated.Value.ShipToAddresses.Should().ContainSingle(address =>
            address.Code == "MAIN" && address.RecipientName == "Updated Main" && address.IsActive);
    }

    [Fact]
    public async Task DeleteIsBlockedWhenMasterHistoryIsAttachedAndImportReportsBadRows()
    {
        var item = new Item("CUST-IMP-ITEM", "Imported", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
        var created = await _service.CreateAsync(
            new CustomerInput(
                "CUST-REF",
                "Referenced",
                ShipToAddresses: [new CustomerShipToAddressInput(null, "MAIN", "Recipient", AddressLine1: "One")]),
            "user-1");

        var deleted = await _service.DeleteAsync(created.Value.Id, "user-1");
        var badImport = await _service.ImportAsync(
            "CODE,LEGAL_NAME,LOCALIZED_NAME,TAX_REGISTRATION_NUMBER,EXTERNAL_ERP_IDENTIFIER,EXTERNAL_CHANNEL_IDENTIFIER,CONTACT_NAME,CONTACT_EMAIL,CONTACT_PHONE,BILLING_ADDRESS_LINE_1,BILLING_ADDRESS_LINE_2,BILLING_CITY,BILLING_REGION,BILLING_POSTAL_CODE,BILLING_COUNTRY_CODE,DEFAULT_CARRIER_CODE,DEFAULT_CARRIER_SERVICE_CODE,PRIORITY,PACKAGING_PROFILE,LABEL_PROFILE,ALLOW_PARTIAL_SHIPMENT,NOTES,IS_ACTIVE\nBAD,Customer,,,,,,,,,,,,,,,,not-number,,,,true",
            "user-1");

        deleted.ErrorCode.Should().Be("customer.referenced");
        badImport.ErrorCode.Should().Be("customer.import_invalid");
        (await _context.Customers.CountAsync()).Should().Be(1);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
