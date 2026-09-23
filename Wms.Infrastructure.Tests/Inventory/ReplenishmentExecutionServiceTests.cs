using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class ReplenishmentExecutionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IInventoryReplenishmentPolicyService> _policyService = new();
    private readonly Mock<IWarehouseWorkService> _workService = new();
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _source;
    private readonly Location _destination;
    private readonly InventoryReplenishmentPolicy _policy;
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly ReplenishmentExecutionService _service;

    public ReplenishmentExecutionServiceTests()
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

        _warehouse = new Warehouse("REP-EXEC-WH", "Replenishment execution warehouse");
        _item = new Item("REP-EXEC-ITEM", "Replenishment execution item", "EA");
        _availableStatus = new InventoryStatus(
            "REP_EXEC_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _source = new Location(
            "REP-EXEC-SOURCE",
            "Reserve source",
            _warehouse.Id,
            type: LocationType.Bulk,
            priority: 1);
        _destination = new Location(
            "REP-EXEC-FACE",
            "Pick face",
            _warehouse.Id,
            type: LocationType.PickFace,
            priority: 10);
        _context.AddRange(_source, _destination);
        _context.SaveChanges();

        _policy = new InventoryReplenishmentPolicy(
            _item.Id,
            _warehouse.Id,
            _destination.Id,
            1m,
            20m,
            2m,
            5m,
            10m,
            InventoryPolicyQuantityBasis.PhysicalAvailable,
            _clock.UtcNow.UtcDateTime);
        _context.Add(_policy);

        AddBalance(_destination, 2m);
        AddBalance(_source, 20m);
        _context.SaveChanges();

        _workService
            .Setup(service => service.CreateAsync(
                It.IsAny<WarehouseWorkInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new WarehouseWorkDto(
                501,
                "WORK-501",
                "generated",
                WarehouseWorkType.Replenishment,
                _warehouse.Id,
                "InventoryReplenishmentPolicy",
                _policy.Id.ToString(CultureInfo.InvariantCulture),
                null,
                "REPLENISHMENT",
                30,
                null,
                null,
                null,
                WarehouseWorkStatus.Available,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                null,
                1,
                [],
                false,
                false)));

        _service = new ReplenishmentExecutionService(
            _context,
            _policyService.Object,
            _workService.Object,
            _warehouseAccess.Object,
            _clock,
            NullLogger<ReplenishmentExecutionService>.Instance);
    }

    [Fact]
    public async Task SignalCreatesDeterministicAvailableWorkFromEligibleSource()
    {
        WarehouseWorkInput? captured = null;
        _workService
            .Setup(service => service.CreateAsync(
                It.IsAny<WarehouseWorkInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<WarehouseWorkInput, string, CancellationToken>(
                (input, _, _) => captured = input)
            .ReturnsAsync(Result.Success(new WarehouseWorkDto(
                501,
                "WORK-501",
                "generated",
                WarehouseWorkType.Replenishment,
                _warehouse.Id,
                "InventoryReplenishmentPolicy",
                _policy.Id.ToString(CultureInfo.InvariantCulture),
                null,
                "REPLENISHMENT",
                30,
                null,
                null,
                null,
                WarehouseWorkStatus.Available,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                null,
                1,
                [],
                false,
                false)));
        SetupSignal(shortfall: 8m);

        var result = await _service.GenerateAsync(
            new ReplenishmentGenerationQuery(_warehouse.Id, _policy.Id),
            "planner-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.WorkCreated.Should().Be(1);
        result.Value.Blocked.Should().Be(0);
        result.Value.Plans.Should().ContainSingle();
        result.Value.Plans.Single().Decision.Should().Be("work-created");
        result.Value.Plans.Single().PlannedQuantity.Should().Be(8m);
        captured.Should().NotBeNull();
        captured!.Type.Should().Be(WarehouseWorkType.Replenishment);
        captured.MakeAvailable.Should().BeTrue();
        captured.Lines.Should().HaveCount(1);
        captured.Lines.Single().SourceLocationId.Should().Be(_source.Id);
        captured.Lines.Single().DestinationLocationId.Should().Be(_destination.Id);
        captured.Lines.Single().PlannedQuantity.Should().Be(8m);
        captured.Lines.Single().InventoryStatusId.Should().Be(_availableStatus.Id);
    }

    [Fact]
    public async Task DryRunProducesFingerprintAndEligibilityWithoutCreatingWork()
    {
        SetupSignal(shortfall: 8m);

        var result = await _service.GenerateAsync(
            new ReplenishmentGenerationQuery(_warehouse.Id, _policy.Id, DryRun: true),
            "planner-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.WorkCreated.Should().Be(0);
        result.Value.Blocked.Should().Be(0);
        result.Value.Plans.Should().ContainSingle();
        result.Value.Plans.Single().Decision.Should().Be("dry-run-eligible");
        result.Value.Plans.Single().SourceStateFingerprint.Should().HaveLength(64);
        result.Value.Plans.Single().Lines.Should().ContainSingle();
        _workService.Verify(service => service.CreateAsync(
            It.IsAny<WarehouseWorkInput>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        (await _context.WarehouseWorks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExistingOpenWorkIsReturnedWithoutGeneratingDuplicate()
    {
        SetupSignal(shortfall: 8m);
        var existing = new WarehouseWorkEntity(
            "WORK-OPEN-REP",
            "replenishment:open",
            WarehouseWorkType.Replenishment,
            _warehouse.Id,
            "InventoryReplenishmentPolicy",
            _policy.Id.ToString(CultureInfo.InvariantCulture));
        existing.AddLine(new WarehouseWorkLine(
            1,
            _warehouse.Id,
            _item.Id,
            8m,
            _item.UnitOfMeasure,
            _source.Id,
            _destination.Id,
            inventoryStatusId: _availableStatus.Id));
        existing.MakeAvailable(_clock.UtcNow.UtcDateTime);
        _context.WarehouseWorks.Add(existing);
        await _context.SaveChangesAsync();

        var result = await _service.GenerateAsync(
            new ReplenishmentGenerationQuery(_warehouse.Id, _policy.Id),
            "planner-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.WorkCreated.Should().Be(0);
        result.Value.WorkReused.Should().Be(1);
        result.Value.Plans.Single().Decision.Should().Be("open-work-already-covers-signal");
        result.Value.Plans.Single().WorkId.Should().Be(existing.Id);
        _workService.Verify(service => service.CreateAsync(
                It.IsAny<WarehouseWorkInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FullDestinationCapacityBlocksGenerationBeforeWorkCreation()
    {
        _destination.SetCapacityDimensions(2m, null, null, null, null);
        await _context.SaveChangesAsync();
        SetupSignal(shortfall: 8m);

        var result = await _service.GenerateAsync(
            new ReplenishmentGenerationQuery(_warehouse.Id, _policy.Id),
            "planner-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.WorkCreated.Should().Be(0);
        result.Value.Blocked.Should().Be(1);
        result.Value.Plans.Single().Decision.Should().Be("destination-capacity-blocked");
        _workService.Verify(service => service.CreateAsync(
                It.IsAny<WarehouseWorkInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupSignal(decimal shortfall) =>
        _policyService
            .Setup(service => service.GetSignalsAsync(
                It.IsAny<InventoryReplenishmentSignalQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<InventoryReplenishmentSignalDto>>(
            [
                new(
                    _policy.Id,
                    _item.Id,
                    _item.Sku,
                    _item.Name,
                    _warehouse.Id,
                    _warehouse.Code,
                    _destination.Id,
                    _destination.Code,
                    InventoryPolicyQuantityBasis.PhysicalAvailable,
                    2m,
                    2m,
                    2m,
                    2m,
                    0m,
                    0m,
                    1m,
                    2m,
                    5m,
                    10m,
                    20m,
                    20m,
                    InventoryReplenishmentSignalKind.LowStock,
                    shortfall,
                    _clock.UtcNow.UtcDateTime)
            ]));

    private void AddBalance(Location location, decimal quantity)
    {
        var balance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        balance.Apply(quantity, 0m, allowNegativeStock: false);
        _context.InventoryBalances.Add(balance);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
