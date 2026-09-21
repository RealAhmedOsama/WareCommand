using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Outbound;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Outbound;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Outbound;

public sealed class OutboundExceptionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IInventoryReservationService> _reservations = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly OutboundExceptionService _service;
    private readonly Warehouse _warehouse;

    public OutboundExceptionServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
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

        _warehouse = new Warehouse("OUT-EX-WH", "Outbound exception warehouse");
        _context.Warehouses.Add(_warehouse);
        _context.SaveChanges();

        _unitOfWork = new UnitOfWork(_context, _access.Object);
        _service = new OutboundExceptionService(
            _context,
            _unitOfWork,
            _access.Object,
            _reservations.Object,
            _audit.Object,
            _clock,
            NullLogger<OutboundExceptionService>.Instance);
    }

    [Fact]
    public async Task QueueSupportsAssignmentReviewAndOverdueFiltering()
    {
        var created = await _service.CreateAsync(
            new OutboundExceptionInput(
                _warehouse.Id,
                OutboundExceptionCode.AllocationShortage,
                OutboundExceptionSeverity.Major,
                "released demand has no eligible stock",
                "out-ex-1",
                DueAtUtc: _clock.UtcNow.UtcDateTime.AddMinutes(-5)),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
        created.Value.Status.Should().Be(OutboundExceptionStatus.Open);

        var page = await _service.ListAsync(new OutboundExceptionQuery(
            WarehouseId: _warehouse.Id,
            OverdueOnly: true));
        page.IsSuccess.Should().BeTrue(page.FirstError?.Message);
        page.Value.Items.Should().ContainSingle(item => item.Id == created.Value.Id);
        page.Value.OpenCount.Should().Be(1);
        page.Value.OverdueCount.Should().Be(1);

        var assigned = await _service.AssignAsync(
            created.Value.Id,
            new OutboundExceptionAssignmentInput("picker-1", "PICKING"),
            "supervisor-1");
        assigned.IsSuccess.Should().BeTrue(assigned.FirstError?.Message);
        assigned.Value.OwnerUserId.Should().Be("picker-1");

        var reviewed = await _service.StartReviewAsync(created.Value.Id, "supervisor-1");
        reviewed.IsSuccess.Should().BeTrue(reviewed.FirstError?.Message);
        reviewed.Value.Status.Should().Be(OutboundExceptionStatus.UnderReview);
    }

    [Fact]
    public async Task ResolutionRequiresOverrideAndReplayCannotApplyTwice()
    {
        var created = await _service.CreateAsync(
            new OutboundExceptionInput(
                _warehouse.Id,
                OutboundExceptionCode.LoadMismatch,
                OutboundExceptionSeverity.Critical,
                "package scan does not match the load",
                "out-ex-2"),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);

        var denied = await _service.ResolveAsync(
            created.Value.Id,
            "resolve-denied",
            new OutboundExceptionResolutionInput(
                OutboundExceptionResolution.SupervisorOverride,
                "controlled correction"),
            "supervisor-1");
        denied.IsSuccess.Should().BeFalse();
        denied.ErrorCode.Should().Be("outbound_exception.supervisor_reason_required");

        var input = new OutboundExceptionResolutionInput(
            OutboundExceptionResolution.SupervisorOverride,
            "controlled correction",
            SupervisorOverride: true,
            OverrideReason: "verified load manifest and operator statement");
        var resolved = await _service.ResolveAsync(
            created.Value.Id,
            "resolve-2",
            input,
            "supervisor-1");
        resolved.IsSuccess.Should().BeTrue(resolved.FirstError?.Message);
        resolved.Value.Status.Should().Be(OutboundExceptionStatus.Resolved);

        var replay = await _service.ResolveAsync(
            created.Value.Id,
            "resolve-2",
            input,
            "supervisor-1");
        replay.IsSuccess.Should().BeTrue(replay.FirstError?.Message);
        (await _context.OutboundExceptionCommands.CountAsync()).Should().Be(1);

        var conflict = await _service.ResolveAsync(
            created.Value.Id,
            "resolve-2",
            input with { Reason = "different correction" },
            "supervisor-1");
        conflict.IsSuccess.Should().BeFalse();
        conflict.ErrorCode.Should().Be("outbound_exception.idempotency_conflict");
    }

    [Fact]
    public async Task SensitiveResolutionWithoutSupervisorIsRejectedWithoutClosingException()
    {
        var created = await _service.CreateAsync(
            new OutboundExceptionInput(
                _warehouse.Id,
                OutboundExceptionCode.CancelledDemand,
                OutboundExceptionSeverity.Warning,
                "customer cancellation arrived after release",
                "out-ex-3"),
            "operator-1");
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);

        var result = await _service.ResolveAsync(
            created.Value.Id,
            "resolve-3",
            new OutboundExceptionResolutionInput(
                OutboundExceptionResolution.CancelOrder,
                "cancel the released order"),
            "operator-1");
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("outbound_exception.supervisor_reason_required");
        (await _context.OutboundExceptions.SingleAsync(value => value.Id == created.Value.Id))
            .Status.Should().Be(OutboundExceptionStatus.Open);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
