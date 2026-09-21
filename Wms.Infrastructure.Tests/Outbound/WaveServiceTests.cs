using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.Application.SalesOrders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Outbound;

namespace Wms.Infrastructure.Tests.Outbound;

public sealed class WaveServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<ISalesOrderAllocationService> _allocation = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly WaveService _service;
    private readonly Warehouse _warehouse;
    private readonly Customer _customer;
    private readonly Item _item;
    private readonly SalesOrder _order;
    private readonly SalesOrderLine _line;

    public WaveServiceTests()
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

        _warehouse = new Warehouse("WAVE-WH", "Wave warehouse");
        _customer = new Customer("WAVE-CUST", "Wave customer", defaultCarrierCode: "FAST");
        _item = new Item("WAVE-ITEM", "Wave item", "EA");
        _context.AddRange(_warehouse, _customer, _item);
        _context.SaveChanges();

        _order = new SalesOrder(
            "SO-WAVE-0001",
            _warehouse.Id,
            _warehouse.Code,
            _customer.Id,
            _customer.Code,
            _customer.LegalName,
            _customer.LocalizedName,
            _customer.ContactName,
            _customer.ContactEmail,
            _customer.ContactPhone,
            null,
            null,
            "Wave customer",
            null,
            "EG",
            "Cairo",
            "Cairo",
            "11511",
            "Wave street",
            null,
            null,
            new DateOnly(2026, 9, 21),
            new DateOnly(2026, 9, 22),
            "WAVE-ORDER-1",
            "CHANNEL",
            null,
            100,
            "FAST",
            "NEXT",
            null,
            null,
            true,
            null,
            "seed");
        _line = new SalesOrderLine(
            1,
            _item.Id,
            _item.Sku,
            _item.Name,
            _item.LocalizedName,
            null,
            "EA",
            10m,
            "EA",
            10m,
            1m,
            0,
            "EA -> EA",
            string.Empty);
        _order.ReplaceDraftLines([_line]);
        _order.Confirm("seed", _clock.UtcNow.UtcDateTime);
        _context.Add(_order);
        _context.SaveChanges();

        _service = new WaveService(
            _context,
            _access.Object,
            _allocation.Object,
            _audit.Object,
            _clock,
            NullLogger<WaveService>.Instance);
    }

    [Fact]
    public async Task TemplateAndScheduledRunSelectDemandDeterministically()
    {
        var template = await _service.SaveTemplateAsync(
            null,
            new WaveTemplateInput(
                _warehouse.Id,
                "daily-fast",
                "Daily fast carrier",
                WaveTriggerType.Scheduled,
                Priority: 90,
                Limit: 10,
                ReleaseToWarehouse: false,
                ScheduleCron: "*/5 * * * *",
                CarrierCode: "fast",
                MinimumPriority: 50,
                SourceType: "channel"),
            "planner");

        var run = await _service.RunScheduledAsync(_warehouse.Id, "planner");

        template.IsSuccess.Should().BeTrue(template.Error);
        template.Value.TemplateKey.Should().Be("DAILY-FAST");
        run.IsSuccess.Should().BeTrue(run.Error);
        run.Value.TemplatesExamined.Should().Be(1);
        run.Value.WavesCreated.Should().Be(1);
        run.Value.LinesSelected.Should().Be(1);
        (await _context.Waves.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CreationKeyIsIdempotentAndDoesNotReselectOccupiedDemand()
    {
        var input = new WaveCreateInput(
            _warehouse.Id,
            "manual-wave-1",
            Priority: 80,
            Limit: 10,
            ReleaseToWarehouse: false,
            CarrierCode: "FAST",
            SourceType: "CHANNEL");

        var first = await _service.CreateAsync(input, "planner");
        var duplicate = await _service.CreateAsync(input, "planner");

        first.IsSuccess.Should().BeTrue(first.Error);
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.Id.Should().Be(first.Value.Id);
        duplicate.Value.Lines.Should().ContainSingle(line => line.SalesOrderLineId == _line.Id);
        (await _context.Waves.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ProcessingIsResumableAndRepeatingTheSameKeyDoesNotReallocate()
    {
        var created = await _service.CreateAsync(
            new WaveCreateInput(
                _warehouse.Id,
                "process-wave-1",
                Limit: 10,
                ReleaseToWarehouse: false),
            "planner");
        created.IsSuccess.Should().BeTrue(created.Error);

        _allocation
            .Setup(service => service.AllocateAsync(
                _order.Id,
                It.Is<SalesOrderAllocationCommand>(command => command.LineIds!.Single() == _line.Id),
                "planner",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SalesOrderAllocationResultDto(
                _order.Id,
                _order.DocumentNumber,
                _warehouse.Id,
                SalesOrderStatus.Allocating,
                false,
                true,
                10m,
                10m,
                0m,
                [new SalesOrderAllocationLineDto(
                    _line.Id,
                    _line.LineNumber,
                    _item.Id,
                    _item.Sku,
                    10m,
                    10m,
                    0m,
                    0m,
                    null,
                    null,
                    0m,
                    0m,
                    "fully allocated",
                    [])],
                [],
                "fully allocated")));

        var processed = await _service.ProcessAsync(
            created.Value.Id,
            new WaveProcessInput("process-1", ReleaseToWarehouse: false),
            "planner");
        var duplicate = await _service.ProcessAsync(
            created.Value.Id,
            new WaveProcessInput("process-1", ReleaseToWarehouse: false),
            "planner");

        processed.IsSuccess.Should().BeTrue(processed.Error);
        processed.Value.Status.Should().Be(WaveStatus.Completed);
        processed.Value.Lines.Should().ContainSingle(line =>
            line.Status == WaveLineStatus.Allocated && line.AllocatedQuantity == 10m);
        duplicate.IsSuccess.Should().BeTrue(duplicate.Error);
        duplicate.Value.Status.Should().Be(WaveStatus.Completed);
        _allocation.Verify(service => service.AllocateAsync(
                It.IsAny<int>(),
                It.IsAny<SalesOrderAllocationCommand>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SelectedLineCanBeRemovedBeforeProcessingAndWaveCanThenBeCancelled()
    {
        var created = await _service.CreateAsync(
            new WaveCreateInput(
                _warehouse.Id,
                "cancel-wave-1",
                Limit: 10,
                ReleaseToWarehouse: false),
            "planner");
        created.IsSuccess.Should().BeTrue(created.Error);

        var removed = await _service.RemoveLineAsync(
            created.Value.Id,
            _line.Id,
            new WaveLineRemovalInput("remove-1", "Order was held."),
            "planner");
        var cancelled = await _service.CancelAsync(
            created.Value.Id,
            new WaveCancellationInput("cancel-1", "Operator cancelled the empty wave."),
            "planner");

        removed.IsSuccess.Should().BeTrue(removed.Error);
        removed.Value.Lines.Should().ContainSingle(line => line.Status == WaveLineStatus.Removed);
        cancelled.IsSuccess.Should().BeTrue(cancelled.Error);
        cancelled.Value.Status.Should().Be(WaveStatus.Cancelled);
        cancelled.Value.Lines.Should().ContainSingle(line => line.Status == WaveLineStatus.Removed);
    }

    [Fact]
    public async Task FailedLineCanBeRetriedWithANewProcessKeyWithoutDuplicatingTheWaveLine()
    {
        var created = await _service.CreateAsync(
            new WaveCreateInput(
                _warehouse.Id,
                "resume-wave-1",
                Limit: 10,
                ReleaseToWarehouse: false),
            "planner");
        created.IsSuccess.Should().BeTrue(created.Error);

        var attempts = 0;
        _allocation
            .Setup(service => service.AllocateAsync(
                _order.Id,
                It.IsAny<SalesOrderAllocationCommand>(),
                "planner",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                attempts++;
                return attempts == 1
                    ? Result.Failure<SalesOrderAllocationResultDto>(WmsErrors.BusinessRule(
                        "allocation.shortage",
                        "No allocatable inventory was available."))
                    : Result.Success(CreateSuccessfulAllocationResult());
            });

        var failed = await _service.ProcessAsync(
            created.Value.Id,
            new WaveProcessInput("resume-1", ReleaseToWarehouse: false),
            "planner");
        var retried = await _service.ProcessAsync(
            created.Value.Id,
            new WaveProcessInput("resume-2", ReleaseToWarehouse: false),
            "planner");

        failed.IsSuccess.Should().BeTrue(failed.Error);
        failed.Value.Status.Should().Be(WaveStatus.Failed);
        retried.IsSuccess.Should().BeTrue(retried.Error);
        retried.Value.Status.Should().Be(WaveStatus.Completed);
        retried.Value.Lines.Should().ContainSingle(line => line.Status == WaveLineStatus.Allocated);
        attempts.Should().Be(2);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private SalesOrderAllocationResultDto CreateSuccessfulAllocationResult() =>
        new(
            _order.Id,
            _order.DocumentNumber,
            _warehouse.Id,
            SalesOrderStatus.Allocating,
            false,
            true,
            10m,
            10m,
            0m,
            [new SalesOrderAllocationLineDto(
                _line.Id,
                _line.LineNumber,
                _item.Id,
                _item.Sku,
                10m,
                10m,
                0m,
                0m,
                null,
                null,
                0m,
                0m,
                "fully allocated",
                [])],
            [],
            "fully allocated");

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
