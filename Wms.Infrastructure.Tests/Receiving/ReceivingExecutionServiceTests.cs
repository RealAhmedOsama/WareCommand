using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Identification;
using Wms.Application.LicensePlates;
using Wms.Application.Receiving;
using Wms.Application.Units;
using Wms.Application.UseCases.Receiving;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Receiving;

namespace Wms.Infrastructure.Tests.Receiving;

public sealed class ReceivingExecutionServiceTests : IDisposable
{
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IIdentificationService> _identification = new();
    private readonly Mock<IItemQuantityConversionService> _quantityConversion = new();
    private readonly Mock<IReceiveItemUseCase> _receive = new();
    private readonly ReceivingExecutionService _service;
    private readonly Warehouse _warehouse;
    private readonly Location _receivingLocation;
    private readonly DateTime _now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    public ReceivingExecutionServiceTests()
    {
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _context.Database.EnsureCreated();

        _access
            .Setup(value => value.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _audit
            .Setup(value => value.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("EXEC-WH", "Execution Warehouse");
        _context.Warehouses.Add(_warehouse);
        _context.SaveChanges();
        _receivingLocation = new Location(
            "EXEC-RECEIVE",
            "Execution Receiving",
            _warehouse.Id,
            type: LocationType.Receiving,
            isPickable: false,
            isReceivable: true);
        _context.Locations.Add(_receivingLocation);
        _context.SaveChanges();

        _service = new ReceivingExecutionService(
            _context,
            _access.Object,
            new FixedClock(_now),
            _audit.Object,
            _identification.Object,
            _quantityConversion.Object,
            Mock.Of<ILicensePlateService>(),
            Mock.Of<IReceiptService>(),
            _receive.Object,
            NullLogger<ReceivingExecutionService>.Instance);
    }

    [Fact]
    public async Task StartAsync_IsIdempotentForTheSameOperatorReference()
    {
        var input = new ReceivingSessionStartInput(
            _warehouse.Id,
            _receivingLocation.Id,
            ReceivingSessionSourceType.BlindReceipt,
            SessionReference: "exec-001",
            SupervisorOverride: true,
            SupervisorOverrideReason: "Unexpected inbound delivery");

        var first = await _service.StartAsync(input, "receiver-1");
        var replay = await _service.StartAsync(input, "receiver-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Id.Should().Be(first.Value.Id);
        (await _context.ReceivingSessions.CountAsync()).Should().Be(1);
        first.Value.LastActivityAtUtc.Should().Be(_now);
    }

    [Fact]
    public async Task Lifecycle_PersistsPauseAndResumeWithoutAllowingScanWhilePaused()
    {
        var started = await _service.StartAsync(
            new ReceivingSessionStartInput(
                _warehouse.Id,
                _receivingLocation.Id,
                ReceivingSessionSourceType.BlindReceipt,
                SessionReference: "exec-002",
                SupervisorOverride: true,
                SupervisorOverrideReason: "Manual exception"),
            "receiver-1");

        var paused = await _service.PauseAsync(started.Value.Id, "receiver-1");
        paused.IsSuccess.Should().BeTrue(paused.Error);
        paused.Value.Status.Should().Be(ReceivingSessionStatus.Paused);

        var rejected = await _service.ScanAsync(
            started.Value.Id,
            new ReceivingScanInput("op-1", "SKU-1", ItemSku: "SKU-1"),
            "receiver-1");
        rejected.IsFailure.Should().BeTrue();
        rejected.ErrorCode.Should().Be("receiving.session_not_open");

        var resumed = await _service.ResumeAsync(started.Value.Id, "receiver-1");
        resumed.IsSuccess.Should().BeTrue(resumed.Error);
        resumed.Value.Status.Should().Be(ReceivingSessionStatus.Open);
    }

    [Fact]
    public async Task ScanAsync_PersistsTheLedgerAndReplaysTheSameClientOperation()
    {
        var item = new Item("EXEC-ITEM", "Execution Item", "EA");
        _context.Items.Add(item);
        await _context.SaveChangesAsync();

        _identification
            .Setup(value => value.ResolveAsync(
                It.IsAny<IdentificationLookupRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IdentificationResolutionDto>(WmsErrors.NotFound(
                "identifier.not_found",
                "No identifier is registered.")));
        _quantityConversion
            .Setup(value => value.ConvertToBaseAsync(
                item.Id,
                2m,
                It.IsAny<string?>(),
                It.IsAny<QuantityRoundingMode?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new QuantityConversionResult(
                2m,
                "EA",
                2m,
                "EA",
                1m,
                4,
                QuantityRoundingMode.Reject,
                0m,
                "test",
                string.Empty)));
        _receive
            .Setup(value => value.ExecuteAsync(
                It.IsAny<Wms.Application.DTOs.ReceiveItemDto>(),
                "receiver-1",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new Wms.Application.DTOs.ReceiptResultDto(
                501,
                item.Sku,
                _receivingLocation.Code,
                2m,
                null,
                _now)));

        var started = await _service.StartAsync(
            new ReceivingSessionStartInput(
                _warehouse.Id,
                _receivingLocation.Id,
                ReceivingSessionSourceType.BlindReceipt,
                SessionReference: "exec-scan",
                SupervisorOverride: true,
                SupervisorOverrideReason: "Manual exception"),
            "receiver-1");

        var input = new ReceivingScanInput("op-1", "EXEC-ITEM", ItemSku: item.Sku, Quantity: 2m);
        var first = await _service.ScanAsync(started.Value.Id, input, "receiver-1");
        var replay = await _service.ScanAsync(started.Value.Id, input, "receiver-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        first.Value.Scans.Should().ContainSingle(scan =>
            scan.ClientOperationId == "op-1" &&
            scan.Status == ReceivingScanStatus.Completed &&
            scan.BaseQuantity == 2m);
        first.Value.Lines.Should().ContainSingle(line => line.ReceivedBaseQuantity == 2m);
        _receive.Verify(value => value.ExecuteAsync(
            It.IsAny<Wms.Application.DTOs.ReceiveItemDto>(),
            "receiver-1",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTimeOffset UtcNow => new(utcNow, TimeSpan.Zero);
    }
}
