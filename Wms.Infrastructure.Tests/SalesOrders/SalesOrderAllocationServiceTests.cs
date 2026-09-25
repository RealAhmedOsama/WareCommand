using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.SalesOrders;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.SalesOrders;
using Wms.Infrastructure.WarehouseWork;

namespace Wms.Infrastructure.Tests.SalesOrders;

public sealed class SalesOrderAllocationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly InventoryLedgerService _ledger;
    private readonly InventoryReservationService _reservations;
    private readonly WarehouseWorkService _work;
    private readonly SalesOrderAllocationService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _pickLocation;
    private readonly Customer _customer;

    public SalesOrderAllocationServiceTests()
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

        _warehouse = new Warehouse("ALLOC-WH", "Allocation Warehouse");
        _item = new Item("ALLOC-ITEM", "Allocation Item", "EA");
        _availableStatus = new InventoryStatus(
            "ALLOC_AVAILABLE",
            "Allocation available",
            "متاح للحجز",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _customer = new Customer("ALLOC-CUST", "Allocation Customer", allowPartialShipment: true);
        _context.AddRange(_warehouse, _item, _availableStatus, _customer);
        _context.SaveChanges();
        _pickLocation = new Location("ALLOC-PICK", "Allocation pick face", _warehouse.Id);
        _context.Add(_pickLocation);
        _context.SaveChanges();

        var unitOfWork = new UnitOfWork(_context, _access.Object);
        _ledger = new InventoryLedgerService(
            unitOfWork,
            _context,
            _access.Object,
            _clock);
        _reservations = new InventoryReservationService(
            unitOfWork,
            _context,
            _ledger,
            _access.Object,
            _clock,
            NullLogger<InventoryReservationService>.Instance);
        _work = new WarehouseWorkService(
            _context,
            unitOfWork,
            _access.Object,
            _audit.Object,
            _clock,
            [],
            NullLogger<WarehouseWorkService>.Instance);
        _service = new SalesOrderAllocationService(
            _context,
            _access.Object,
            _reservations,
            _work,
            _audit.Object,
            NullLogger<SalesOrderAllocationService>.Instance);
    }

    [Fact]
    public async Task PartialAllocationProducesBackorderAndReleasesPickWorkExactlyOnce()
    {
        await SeedInventoryAsync(5m, "alloc-receipt-1");
        var order = await CreateOrderAsync(8m, allowPartialShipment: true);
        var allocationCommand = new SalesOrderAllocationCommand(
            ReleaseToWarehouse: false,
            IdempotencyKey: "alloc-command-1");

        var allocated = await _service.AllocateAsync(
            order.Id,
            allocationCommand,
            "allocator-1");
        var replayed = await _service.AllocateAsync(
            order.Id,
            allocationCommand,
            "allocator-1");

        allocated.IsSuccess.Should().BeTrue(allocated.Error);
        allocated.Value.OrderStatus.Should().Be(SalesOrderStatus.PartiallyAllocated);
        allocated.Value.Lines.Single().AllocatedBaseQuantity.Should().Be(5m);
        allocated.Value.Lines.Single().BackorderBaseQuantity.Should().Be(3m);
        replayed.IsSuccess.Should().BeTrue(replayed.Error);
        (await _context.InventoryReservations.CountAsync()).Should().Be(1);
        (await _context.InventoryReservationAllocations.CountAsync()).Should().Be(1);

        var released = await _service.ReleaseAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "release-command-1"),
            "allocator-1");
        var releaseReplay = await _service.ReleaseAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "release-command-1"),
            "allocator-1");

        released.IsSuccess.Should().BeTrue(released.Error);
        released.Value.OrderStatus.Should().Be(SalesOrderStatus.Released);
        released.Value.Work.Should().ContainSingle(work => work.PlannedQuantity == 5m);
        releaseReplay.IsSuccess.Should().BeTrue(releaseReplay.Error);
        (await _context.WarehouseWorks.CountAsync()).Should().Be(1);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(5m);
    }

    [Fact]
    public async Task AllocationReturnsRetryableConcurrencyErrorForReservationVersionConflict()
    {
        var order = await CreateOrderAsync(1m, allowPartialShipment: true);
        var reservations = new Mock<IInventoryReservationService>();
        reservations
            .Setup(service => service.ReserveAsync(
                It.IsAny<InventoryReservationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException(
                nameof(InventoryReservation),
                "Id=41"));
        var service = new SalesOrderAllocationService(
            _context,
            _access.Object,
            reservations.Object,
            _work,
            _audit.Object,
            NullLogger<SalesOrderAllocationService>.Instance);

        var result = await service.AllocateAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "allocate-concurrency-conflict"),
            "allocator-1");

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Type.Should().Be(ErrorType.Concurrency);
        result.FirstError.Code.Should().Be("data.concurrency_conflict");
        result.FirstError.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task AllocationRetriesInventoryBalanceContentionAndReconcilesBackorder()
    {
        await SeedInventoryAsync(2m, "alloc-receipt-contention");
        var order = await CreateOrderAsync(3m, allowPartialShipment: true);
        var attempts = 0;
        var reservations = new Mock<IInventoryReservationService>();
        reservations
            .Setup(service => service.ReserveAsync(
                It.IsAny<InventoryReservationRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (InventoryReservationRequest request, CancellationToken cancellationToken) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new ConcurrencyConflictException(nameof(InventoryBalance), "Id=41");
                }

                return await _reservations.ReserveAsync(request, cancellationToken);
            });
        reservations
            .Setup(service => service.GetByDemandAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns((string demandType, string demandId, int? demandLine, int warehouseId, CancellationToken cancellationToken) =>
                _reservations.GetByDemandAsync(
                    demandType,
                    demandId,
                    demandLine,
                    warehouseId,
                    cancellationToken));
        var service = new SalesOrderAllocationService(
            _context,
            _access.Object,
            reservations.Object,
            _work,
            _audit.Object,
            NullLogger<SalesOrderAllocationService>.Instance);

        var result = await service.AllocateAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "allocate-contention-retry"),
            "allocator-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Lines.Single().AllocatedBaseQuantity.Should().Be(2m);
        result.Value.Lines.Single().BackorderBaseQuantity.Should().Be(1m);
        attempts.Should().Be(2);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(2m);
        (await _context.InventoryReservations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SimulationExplainsShortageWithoutChangingReservationsOrOrder()
    {
        await SeedInventoryAsync(2m, "alloc-receipt-2");
        var order = await CreateOrderAsync(4m, allowPartialShipment: true);

        var result = await _service.SimulateAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "simulation-1"),
            "allocator-1");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.IsSimulation.Should().BeTrue();
        result.Value.OrderStatus.Should().Be(SalesOrderStatus.Confirmed);
        result.Value.Lines.Single().AllocatedBaseQuantity.Should().Be(2m);
        result.Value.Lines.Single().BackorderBaseQuantity.Should().Be(2m);
        result.Value.Lines.Single().Explanation.Should().Contain("backorder");
        result.Value.Work.Should().BeEmpty();
        (await _context.InventoryReservations.CountAsync()).Should().Be(0);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task UnreleaseRestoresAvailabilityAndReplanCreatesNewAllocationWork()
    {
        await SeedInventoryAsync(4m, "alloc-receipt-3");
        var order = await CreateOrderAsync(4m, allowPartialShipment: false);

        var released = await _service.AllocateAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "allocate-release-1"),
            "allocator-1");
        released.IsSuccess.Should().BeTrue(released.Error);
        released.Value.OrderStatus.Should().Be(SalesOrderStatus.Released);

        var unreleased = await _service.UnreleaseAsync(
            order.Id,
            new SalesOrderAllocationCommand(IdempotencyKey: "unrelease-1"),
            "allocator-1");

        unreleased.IsSuccess.Should().BeTrue(unreleased.Error);
        unreleased.Value.OrderStatus.Should().Be(SalesOrderStatus.Confirmed);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(0m);
        (await _context.WarehouseWorks.CountAsync(work => work.Status == WarehouseWorkStatus.Cancelled))
            .Should().Be(1);

        var replanned = await _service.ReallocateAsync(
            order.Id,
            new SalesOrderAllocationCommand(
                ReleaseToWarehouse: true,
                IdempotencyKey: "replan-1"),
            "allocator-1");

        replanned.IsSuccess.Should().BeTrue(replanned.Error);
        replanned.Value.OrderStatus.Should().Be(SalesOrderStatus.Released);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(4m);
        (await _context.WarehouseWorks.CountAsync(work => work.Status == WarehouseWorkStatus.Available))
            .Should().Be(1);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedInventoryAsync(decimal quantity, string idempotencyKey)
    {
        await _ledger.RecordAsync(
            [
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    new InventoryBalanceKey(
                        _warehouse.Id,
                        _pickLocation.Id,
                        _item.Id,
                        null,
                        null,
                        null,
                        null,
                        _availableStatus.Id,
                        _item.UnitOfMeasure),
                    quantity,
                    ActorUserId: "receiver-1",
                    IdempotencyKey: idempotencyKey,
                    TransactionGroupId: $"{idempotencyKey}-group")
            ]);
        await _context.SaveChangesAsync();
    }

    private async Task<SalesOrder> CreateOrderAsync(
        decimal quantity,
        bool allowPartialShipment)
    {
        var order = new SalesOrder(
            $"SO-ALLOC-{Guid.NewGuid():N}"[..20],
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
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new DateOnly(2026, 9, 21),
            null,
            null,
            "MANUAL",
            null,
            10,
            null,
            null,
            null,
            null,
            allowPartialShipment,
            null,
            "creator-1");
        var line = new SalesOrderLine(
            1,
            _item.Id,
            _item.Sku,
            _item.Name,
            _item.LocalizedName,
            null,
            _item.UnitOfMeasure,
            quantity,
            _item.UnitOfMeasure,
            quantity,
            1m,
            0,
            "EA",
            string.Empty);
        order.ReplaceDraftLines([line]);
        order.Confirm("creator-1", _clock.UtcNow.UtcDateTime);
        _context.SalesOrders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
