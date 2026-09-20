using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Units;

namespace Wms.Infrastructure.Tests.Units;

public sealed class UnitOfMeasureServiceTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private WmsDbContext _context = null!;
    private UnitOfMeasureService _service = null!;
    private int _locationId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        await _context.Database.EnsureCreatedAsync();

        var warehouse = new Warehouse("TEST", "Test Warehouse");
        _context.Warehouses.Add(warehouse);
        await _context.SaveChangesAsync();
        var location = new Location("TEST-BIN", "Test Bin", warehouse.Id);
        _context.Locations.Add(location);
        await _context.SaveChangesAsync();
        _locationId = location.Id;

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

        _service = new UnitOfMeasureService(
            _context,
            _access.Object,
            _audit.Object,
            NullLogger<UnitOfMeasureService>.Instance);
    }

    [Fact]
    public async Task Assignment_ResolvesMultiLevelConversionAndStoresMovementSnapshot()
    {
        await AddUnitsAsync(
            new("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each"),
            new("CASE", UnitOfMeasureCategory.Count, 0, "case", "Case"),
            new("PALLET", UnitOfMeasureCategory.Count, 0, "pallet", "Pallet"));
        var item = new Item("ITEM-001", "Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var assignment = await _service.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                item.Id,
                "EA",
                "PALLET",
                "CASE",
                false,
                [
                    new("PALLET", "CASE", 10m, 0),
                    new("CASE", "EA", 12m, 0)
                ]),
            "user-1");

        assignment.IsSuccess.Should().BeTrue();
        var conversion = await _service.ConvertToBaseAsync(item.Id, 2m, "PALLET");
        conversion.IsSuccess.Should().BeTrue();
        conversion.Value.BaseQuantity.Should().Be(240m);
        conversion.Value.ConversionFactorToBase.Should().Be(120m);
        conversion.Value.ConversionPath.Should().Be("PALLET -> CASE -> EA");

        var movement = Movement.CreateReceipt(
            item.Id,
            _locationId,
            new Quantity(conversion.Value.BaseQuantity, conversion.Value.ToSnapshot()),
            "user-1");
        _context.Movements.Add(movement);
        await _context.SaveChangesAsync();

        var saved = await _context.Movements.SingleAsync();
        saved.Quantity.Value.Should().Be(240m);
        saved.EnteredQuantity.Should().Be(2m);
        saved.EnteredUnitOfMeasure.Should().Be("PALLET");
        saved.BaseUnitOfMeasure.Should().Be("EA");
        saved.ConversionFactorToBase.Should().Be(120m);
        saved.ConversionRuleIds.Should().Contain("|");
    }

    [Fact]
    public async Task Conversion_RejectsRoundingAndIncompatibleCategories()
    {
        await AddUnitsAsync(
            new("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each"),
            new("CASE", UnitOfMeasureCategory.Count, 0, "case", "Case"),
            new("KG", UnitOfMeasureCategory.Weight, 3, "kg", "Kilogram"));
        var item = new Item("ITEM-001", "Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var assignment = await _service.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                item.Id,
                "EA",
                "CASE",
                "EA",
                true,
                [new("CASE", "EA", 2.5m, 0)]),
            "user-1");
        assignment.IsSuccess.Should().BeTrue();

        var rounding = await _service.ConvertToBaseAsync(item.Id, 1m, "CASE");
        rounding.IsFailure.Should().BeTrue();
        rounding.ErrorCode.Should().Be("quantity.rounding_required");

        var rounded = await _service.ConvertToBaseAsync(
            item.Id,
            1m,
            "CASE",
            QuantityRoundingMode.AwayFromZero);
        rounded.IsSuccess.Should().BeTrue();
        rounded.Value.BaseQuantity.Should().Be(3m);

        var invalid = await _service.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                item.Id,
                "EA",
                "EA",
                "EA",
                true,
                [new("KG", "EA", 1m, 0)]),
            "user-1");
        invalid.IsFailure.Should().BeTrue();
        invalid.ErrorCode.Should().Be("uom.conversion_category_mismatch");
    }

    [Fact]
    public async Task MultiLevelConversion_AppliesEachRulePrecisionBeforeNextStep()
    {
        await AddUnitsAsync(
            new("EA", UnitOfMeasureCategory.Count, 2, "ea", "Each"),
            new("CASE", UnitOfMeasureCategory.Count, 1, "case", "Case"),
            new("PALLET", UnitOfMeasureCategory.Count, 0, "pallet", "Pallet"));
        var item = new Item("ITEM-001", "Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var assignment = await _service.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                item.Id,
                "EA",
                "PALLET",
                "EA",
                true,
                [
                    new("PALLET", "CASE", 2.55m, 1, QuantityRoundingMode.ToEven),
                    new("CASE", "EA", 2m, 2, QuantityRoundingMode.ToEven)
                ]),
            "user-1");
        assignment.IsSuccess.Should().BeTrue();

        var conversion = await _service.ConvertToBaseAsync(item.Id, 1m, "PALLET");

        conversion.IsSuccess.Should().BeTrue();
        conversion.Value.BaseQuantity.Should().Be(5.2m);
        conversion.Value.ConversionFactorToBase.Should().Be(5.1m);
        conversion.Value.ResultPrecision.Should().Be(2);
        conversion.Value.RoundingDelta.Should().Be(0.05m);
    }

    [Fact]
    public async Task UsedConversion_IsVersionedInsteadOfMutatingHistory()
    {
        await AddUnitsAsync(
            new("EA", UnitOfMeasureCategory.Count, 0, "ea", "Each"),
            new("CASE", UnitOfMeasureCategory.Count, 0, "case", "Case"));
        var item = new Item("ITEM-001", "Widget", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        var first = await _service.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                item.Id,
                "EA",
                "CASE",
                "EA",
                true,
                [new("CASE", "EA", 12m, 0)]),
            "user-1");
        first.IsSuccess.Should().BeTrue();
        var converted = await _service.ConvertToBaseAsync(item.Id, 1m, "CASE");
        var movement = Movement.CreateReceipt(
            item.Id,
            _locationId,
            new Quantity(converted.Value.BaseQuantity, converted.Value.ToSnapshot()),
            "user-1");
        _context.Movements.Add(movement);
        await _context.SaveChangesAsync();

        var second = await _service.SaveItemAssignmentAsync(
            new ItemUnitAssignmentRequest(
                item.Id,
                "EA",
                "CASE",
                "EA",
                true,
                [new("CASE", "EA", 10m, 0)]),
            "user-1");

        second.IsSuccess.Should().BeTrue();
        var rules = await _context.ItemUnitConversions
            .Where(rule => rule.ItemId == item.Id)
            .OrderBy(rule => rule.Version)
            .ToListAsync();
        rules.Should().HaveCount(2);
        rules[0].ConversionFactor.Should().Be(12m);
        rules[0].IsActive.Should().BeFalse();
        rules[1].ConversionFactor.Should().Be(10m);
        rules[1].Version.Should().Be(2);
        rules[1].IsActive.Should().BeTrue();
    }

    private async Task AddUnitsAsync(params UnitOfMeasure[] units)
    {
        _context.UnitOfMeasures.AddRange(units);
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
