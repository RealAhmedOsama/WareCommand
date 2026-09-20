using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Identification;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Identification;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identification;

namespace Wms.Infrastructure.Tests.Identification;

public sealed class IdentificationServiceTests : IAsyncLifetime, IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IClock> _clock = new();
    private WmsDbContext _context = null!;
    private IdentificationRegistry _registry = null!;
    private IdentificationService _service = null!;

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
        _clock
            .SetupGet(clock => clock.UtcNow)
            .Returns(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));

        _registry = new IdentificationRegistry(_context);
        _service = new IdentificationService(
            _context,
            _access.Object,
            _audit.Object,
            _clock.Object);
    }

    [Fact]
    public async Task RegistryAndResolver_DistinguishItemPackagingAndGs1Metadata()
    {
        var item = new Item("ITEM-001", "Widget", "EA");
        item.AddBarcode(new Wms.Domain.ValueObjects.Barcode("ITEM-LABEL-1"));
        item.AddPackaging(new ItemPackaging(
            "CASE",
            "EA",
            12m,
            barcode: "CASE-LABEL-1",
            gtin: "00012345678905"));
        _context.Items.Add(item);
        (await _registry.SyncItemAsync(item)).IsSuccess.Should().BeTrue();
        await _context.SaveChangesAsync();

        var itemResult = await _service.ResolveAsync(new IdentificationLookupRequest("item-label-1"));
        itemResult.IsSuccess.Should().BeTrue();
        itemResult.Value.Kind.Should().Be(IdentificationKind.Item);
        itemResult.Value.ItemSku.Should().Be("ITEM-001");

        var packagingResult = await _service.ResolveAsync(new IdentificationLookupRequest(
            "(01)00012345678905(17)251231(10)LOT-A(21)SER-9(30)12"));
        packagingResult.IsSuccess.Should().BeTrue();
        packagingResult.Value.Kind.Should().Be(IdentificationKind.Packaging);
        packagingResult.Value.PackagingCode.Should().Be("CASE");
        packagingResult.Value.Gs1!.Lot.Should().Be("LOT-A");
        packagingResult.Value.Gs1.Quantity.Should().Be(12m);

        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record =>
                record.Action == WmsAuditActions.IdentifierResolved &&
                (record.Details ?? string.Empty).Contains("00012345678905", StringComparison.Ordinal) == false),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Registry_RejectsGlobalReuseAndSupportsInactiveLifecycle()
    {
        var first = await _registry.RegisterAsync(new IdentificationRegistrationRequest(
            "LPN-001",
            IdentificationKind.LicensePlate,
            IdentifierOwnerKeys.LicensePlate("LPN-001")));
        first.IsSuccess.Should().BeTrue();
        await _context.SaveChangesAsync();

        var conflict = await _registry.RegisterAsync(new IdentificationRegistrationRequest(
            "lpn-001",
            IdentificationKind.LicensePlate,
            "LPN:OTHER"));
        conflict.IsFailure.Should().BeTrue();
        conflict.ErrorCode.Should().Be("identification.conflict");

        first.Value.UpdateLifecycle(false);
        await _context.SaveChangesAsync();
        var hidden = await _service.ResolveAsync(new IdentificationLookupRequest("LPN-001"));
        hidden.ErrorCode.Should().Be("identification.not_found");
        var included = await _service.ResolveAsync(new IdentificationLookupRequest("LPN-001", IncludeInactive: true));
        included.IsSuccess.Should().BeTrue();
        included.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Resolver_FallsBackToLegacyItemBarcodeDuringMigrationWindow()
    {
        var item = new Item("LEGACY-001", "Legacy", "EA");
        item.AddBarcode(new Wms.Domain.ValueObjects.Barcode("LEGACY-BC-1"));
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var result = await _service.ResolveAsync(new IdentificationLookupRequest("legacy-bc-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Kind.Should().Be(IdentificationKind.Item);
        result.Value.ItemSku.Should().Be("LEGACY-001");
    }

    [Fact]
    public async Task Resolver_DistinguishesLocationsAndRejectsLegacyCrossWarehouseAmbiguity()
    {
        var firstWarehouse = new Warehouse("SCAN-A", "Scan A");
        var secondWarehouse = new Warehouse("SCAN-B", "Scan B");
        _context.Warehouses.AddRange(firstWarehouse, secondWarehouse);
        await _context.SaveChangesAsync();

        var firstLocation = new Location(
            "BIN-01",
            "First bin",
            firstWarehouse.Id,
            barcode: "LOCATION-LABEL-1");
        _context.Locations.Add(firstLocation);
        (await _registry.SyncLocationAsync(firstLocation)).IsSuccess.Should().BeTrue();
        await _context.SaveChangesAsync();

        var resolved = await _service.ResolveAsync(new IdentificationLookupRequest("location-label-1"));
        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Kind.Should().Be(IdentificationKind.Location);
        resolved.Value.LocationCode.Should().Be("BIN-01");
        resolved.Value.WarehouseId.Should().Be(firstWarehouse.Id);

        var duplicateLocation = new Location(
            "BIN-01",
            "Second bin",
            secondWarehouse.Id,
            barcode: "LOCATION-LABEL-1");
        _context.Locations.Add(duplicateLocation);
        await _context.SaveChangesAsync();

        var ambiguous = await _service.ResolveAsync(new IdentificationLookupRequest("location-label-1"));
        ambiguous.ErrorCode.Should().Be("identification.ambiguous");
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
    }
}
