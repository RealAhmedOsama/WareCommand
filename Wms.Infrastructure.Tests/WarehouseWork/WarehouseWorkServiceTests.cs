using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.WarehouseWork;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.WarehouseWork;

public sealed class WarehouseWorkServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FakeCompletionHandler _handler = new();
    private readonly WarehouseWorkService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;

    public WarehouseWorkServiceTests()
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

        _service = new WarehouseWorkService(
            _context,
            _access.Object,
            _audit.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            [_handler],
            NullLogger<WarehouseWorkService>.Instance);

        _warehouse = new Warehouse("WORK-WH", "Work Warehouse");
        _item = new Item("WORK-ITEM", "Work Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();
    }

    [Fact]
    public async Task CreateWithSameCreationKeyReturnsOneWorkItem()
    {
        var input = CreateInput("create-once");

        var first = await _service.CreateAsync(input, "creator-1");
        var second = await _service.CreateAsync(input, "creator-2");

        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsSuccess.Should().BeTrue(second.Error);
        second.Value.Id.Should().Be(first.Value.Id);
        (await _context.WarehouseWorks.CountAsync()).Should().Be(1);
        (await _context.WarehouseWorkLines.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task CommandsAndCompletionAreIdempotent()
    {
        var created = await _service.CreateAsync(CreateInput("lifecycle"), "creator-1");
        created.IsSuccess.Should().BeTrue(created.Error);
        var workId = created.Value.Id;
        var lineId = created.Value.Lines.Single().Id;

        var assigned = await _service.AssignAsync(
            workId,
            new WarehouseWorkAssignmentInput("worker-1", null, "assign-1"),
            "manager-1");
        assigned.IsSuccess.Should().BeTrue(assigned.Error);

        var started = await _service.StartAsync(
            workId,
            new WarehouseWorkCommandInput("start-1"),
            "worker-1");
        started.IsSuccess.Should().BeTrue(started.Error);

        var completion = new WarehouseWorkCompletionInput(
            "complete-1",
            [new WarehouseWorkLineActualInput(lineId, 10m)]);
        var completed = await _service.CompleteAsync(workId, completion, "worker-1");
        var replayed = await _service.CompleteAsync(workId, completion, "worker-1");

        completed.IsSuccess.Should().BeTrue(completed.Error);
        replayed.IsSuccess.Should().BeTrue(replayed.Error);
        replayed.Value.Status.Should().Be(WarehouseWorkStatus.Completed);
        replayed.Value.Revision.Should().Be(completed.Value.Revision);
        _handler.ExecutionCount.Should().Be(1);
        (await _context.WarehouseWorkCommands.CountAsync()).Should().Be(3);
        (await _context.WarehouseWorks.SingleAsync()).Status.Should().Be(WarehouseWorkStatus.Completed);
    }

    [Fact]
    public async Task CompletionWithoutHandlerReturnsDependencyFailure()
    {
        var created = await _service.CreateAsync(
            CreateInput("missing-handler", WarehouseWorkType.Pick),
            "creator-1");

        var assigned = await _service.AssignAsync(
            created.Value.Id,
            new WarehouseWorkAssignmentInput("worker-1", null, "assign-missing"),
            "manager-1");
        assigned.IsSuccess.Should().BeTrue(assigned.Error);
        var started = await _service.StartAsync(
            created.Value.Id,
            new WarehouseWorkCommandInput("start-missing"),
            "worker-1");
        started.IsSuccess.Should().BeTrue(started.Error);

        var result = await _service.CompleteAsync(
            created.Value.Id,
            new WarehouseWorkCompletionInput("complete-missing"),
            "worker-1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("work.handler_missing");
        (await _context.WarehouseWorks.SingleAsync()).Status.Should().Be(WarehouseWorkStatus.InProgress);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private WarehouseWorkInput CreateInput(
        string creationKey,
        WarehouseWorkType type = WarehouseWorkType.Putaway) => new(
        creationKey,
        type,
        _warehouse.Id,
        "RECEIPT",
        "receipt-1",
        Lines:
        [
            new WarehouseWorkLineInput(
                1,
                _warehouse.Id,
                _item.Id,
                10m,
                "EA")
        ]);

    private sealed class FakeCompletionHandler : IWarehouseWorkCompletionHandler
    {
        public WarehouseWorkType WorkType => WarehouseWorkType.Putaway;
        public int ExecutionCount { get; private set; }

        public Task<Result<WarehouseWorkHandlerResult>> ExecuteAsync(
            WarehouseWorkEntity work,
            WarehouseWorkCompletionInput input,
            string userId,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            var actualLines = input.Lines ?? work.Lines
                .Select(line => new WarehouseWorkLineActualInput(line.Id, line.PlannedQuantity))
                .ToArray();
            return Task.FromResult(Result.Success(new WarehouseWorkHandlerResult(actualLines, "fake movement")));
        }
    }

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
