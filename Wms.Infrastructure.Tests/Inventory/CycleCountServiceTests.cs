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
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.WarehouseWork;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class CycleCountServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _location;
    private readonly CycleCountService _service;

    public CycleCountServiceTests()
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

        _warehouse = new Warehouse("COUNT-WH", "Cycle-count warehouse");
        _item = new Item("COUNT-ITEM", "Cycle-count item", "EA");
        _availableStatus = new InventoryStatus(
            "COUNT_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _location = new Location(
            "COUNT-BIN",
            "Count bin",
            _warehouse.Id,
            type: LocationType.Bin,
            isReceivable: false);
        _context.Add(_location);
        _context.SaveChanges();

        var balance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        balance.Apply(7m, 0m, allowNegativeStock: false);
        _context.Add(balance);
        _context.SaveChanges();

        var workService = new WarehouseWorkService(
            _context,
            new UnitOfWork(_context, _access.Object),
            _access.Object,
            _audit.Object,
            _clock,
            Array.Empty<IWarehouseWorkCompletionHandler>(),
            NullLogger<WarehouseWorkService>.Instance);
        _service = new CycleCountService(
            _context,
            workService,
            _access.Object,
            _audit.Object,
            _clock,
            NullLogger<CycleCountService>.Instance);
    }

    [Fact]
    public async Task BlindDuePlanCreatesSnapshotAndCountWorkWithoutExpectedQuantityInSummary()
    {
        var plan = await _service.SavePlanAsync(
            null,
            new CycleCountPlanInput(
                "COUNT-DAILY",
                _warehouse.Id,
                _location.Id,
                _item.Id,
                null,
                1,
                0m,
                Blind: true,
                CycleCountFreezePolicy.SnapshotAndReconcile,
                _clock.UtcNow.UtcDateTime.AddHours(-1)),
            "planner-1");
        plan.IsSuccess.Should().BeTrue(plan.Error);

        var result = await _service.GenerateAsync(
            new CycleCountGenerationQuery(_warehouse.Id, plan.Value.Id),
            "planner-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.PlansExamined.Should().Be(1);
        result.Value.TasksCreated.Should().Be(1);
        result.Value.LinesCreated.Should().Be(1);
        result.Value.Tasks.Should().ContainSingle();
        result.Value.Tasks.Single().Blind.Should().BeTrue();
        result.Value.Tasks.Single().ExpectedQuantity.Should().BeNull();
        result.Value.Tasks.Single().WarehouseWorkId.Should().NotBeNull();

        var task = await _context.CycleCountTasks
            .Include(value => value.Lines)
            .SingleAsync();
        task.Lines.Single().ExpectedQuantity.Should().Be(7m);
        task.WarehouseWorkId.Should().NotBeNull();
        var work = await _context.WarehouseWorks
            .Include(value => value.Lines)
            .SingleAsync();
        work.Type.Should().Be(WarehouseWorkType.Count);
        work.Lines.Single().PlannedQuantity.Should().Be(1m);
        work.Lines.Single().SourceReference.Should().StartWith("cycle-count-line:");
    }

    [Fact]
    public async Task DuplicatePlanKeysAreRejectedWithinWarehouse()
    {
        var input = new CycleCountPlanInput(
            "COUNT-DUPLICATE",
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            7,
            0m,
            Blind: false,
            CycleCountFreezePolicy.BlockSelectedDimensions,
            _clock.UtcNow.UtcDateTime.AddDays(1));

        var first = await _service.SavePlanAsync(null, input, "planner-1");
        var second = await _service.SavePlanAsync(null, input, "planner-2");

        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("cycle_count.plan_key_conflict");
        (await _context.CycleCountPlans.CountAsync()).Should().Be(1);
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
