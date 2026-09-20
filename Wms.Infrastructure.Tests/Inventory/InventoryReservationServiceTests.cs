using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryReservationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly InventoryReservationService _service;
    private readonly Warehouse _warehouse;
    private readonly Location _location;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;

    public InventoryReservationServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _unitOfWork = new UnitOfWork(_context, _warehouseAccess.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _warehouseAccess.Object,
            _clock);
        _service = new InventoryReservationService(
            _unitOfWork,
            _context,
            _ledger,
            _warehouseAccess.Object,
            _clock,
            NullLogger<InventoryReservationService>.Instance);

        _warehouse = new Warehouse("RES-WH", "Reservation Warehouse");
        _item = new Item("RES-ITEM", "Reservation Item", "EA");
        _availableStatus = new InventoryStatus(
            "RES_AVAILABLE",
            "Reservation available",
            "متاح للحجز",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();
        _location = new Location("RES-A", "Reservation Pick Face", _warehouse.Id);
        _context.Add(_location);
        _context.SaveChanges();
    }

    [Fact]
    public async Task ReserveCreatesTraceablePartialAllocationAndProtectsAvailableQuantity()
    {
        await SeedReceiptAsync(_location, _availableStatus.Id, 5m, "receipt-1");

        var request = CreateRequest("SO-100", requestedQuantity: 8m);
        var first = await _service.ReserveAsync(request);
        var retry = await _service.ReserveAsync(request);

        first.Status.Should().Be(InventoryReservationStatus.PartiallyReserved);
        first.AllocatedQuantity.Should().Be(5m);
        first.ActiveQuantity.Should().Be(5m);
        first.BackorderQuantity.Should().Be(3m);
        first.Allocations.Should().ContainSingle();
        first.Events.Select(reservationEvent => reservationEvent.Type)
            .Should()
            .Contain([InventoryReservationEventType.Created, InventoryReservationEventType.AllocationAdded]);
        retry.Should().BeEquivalentTo(first);

        var balance = await _context.InventoryBalances.SingleAsync();
        balance.OnHandQuantity.Should().Be(5m);
        balance.ReservedQuantity.Should().Be(5m);
        balance.AvailableQuantity.Should().Be(0m);
        (await _context.InventoryReservations.CountAsync()).Should().Be(1);
        (await _context.InventoryReservationAllocations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReleaseRestoresAvailabilityAndRetainsReasonedHistory()
    {
        await SeedReceiptAsync(_location, _availableStatus.Id, 5m, "receipt-2");
        var reserved = await _service.ReserveAsync(CreateRequest("SO-101", 5m));

        var released = await _service.ReleaseAsync(
            new InventoryReservationMutationRequest(
                reserved.ReservationId,
                null,
                "user-1",
                "release-correlation",
                "customer cancelled"));

        released.Status.Should().Be(InventoryReservationStatus.Released);
        released.ActiveQuantity.Should().Be(0m);
        released.ReleasedQuantity.Should().Be(5m);
        released.Events.Should().Contain(eventItem =>
            eventItem.Type == InventoryReservationEventType.Released &&
            eventItem.Quantity == 5m &&
            eventItem.Reason == "customer cancelled");
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(0m);
        (await _context.InventoryTransactions.CountAsync(transaction =>
            transaction.Type == InventoryTransactionType.Release)).Should().Be(1);
    }

    [Fact]
    public async Task ConsumptionDecrementsPhysicalAndReservedQuantitiesExactlyOnce()
    {
        await SeedReceiptAsync(_location, _availableStatus.Id, 5m, "receipt-3");
        var reserved = await _service.ReserveAsync(CreateRequest("SO-102", 3m));

        var partial = await _service.ConsumeAsync(
            new InventoryReservationMutationRequest(
                reserved.ReservationId,
                2m,
                "picker-1",
                "pick-correlation",
                "picked two"));
        var completed = await _service.ConsumeAsync(
            new InventoryReservationMutationRequest(
                reserved.ReservationId,
                null,
                "picker-1",
                "pick-correlation-2",
                "picked remainder"));

        partial.Status.Should().Be(InventoryReservationStatus.PartiallyConsumed);
        partial.ConsumedQuantity.Should().Be(2m);
        partial.ActiveQuantity.Should().Be(1m);
        completed.Status.Should().Be(InventoryReservationStatus.Consumed);
        completed.ConsumedQuantity.Should().Be(3m);
        completed.ActiveQuantity.Should().Be(0m);
        completed.Events.Count(eventItem => eventItem.Type == InventoryReservationEventType.Consumed)
            .Should().Be(2);

        var balance = await _context.InventoryBalances.SingleAsync();
        balance.OnHandQuantity.Should().Be(2m);
        balance.ReservedQuantity.Should().Be(0m);
        (await _context.InventoryTransactions.CountAsync(transaction =>
            transaction.Type == InventoryTransactionType.Pick)).Should().Be(2);
    }

    [Fact]
    public async Task NonAllocatableStatusIsExcludedFromReservationCandidates()
    {
        var heldStatus = new InventoryStatus(
            "RES_HOLD",
            "Reservation hold",
            "معلق",
            isAvailable: false,
            isAllocatable: false,
            isPickable: false,
            isShippable: false,
            isCountable: true);
        _context.InventoryStatuses.Add(heldStatus);
        await _context.SaveChangesAsync();
        await SeedReceiptAsync(_location, heldStatus.Id, 4m, "receipt-hold");

        var result = await _service.ReserveAsync(CreateRequest("SO-103", 2m));

        result.Status.Should().Be(InventoryReservationStatus.Pending);
        result.AllocatedQuantity.Should().Be(0m);
        result.BackorderQuantity.Should().Be(2m);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task ExpiredLotsHeldSerialsAndClosedLicensePlatesAreExcluded()
    {
        var expiredLot = new Lot(
            "LOT-EXPIRED",
            _item.Id,
            expiryDate: _clock.UtcNow.UtcDateTime.AddDays(-1));
        var heldSerial = new SerialNumber("SER-HELD", _item.Id);
        var closedLicensePlate = new LicensePlate(
            "LP-CLOSED",
            LicensePlateType.Pallet,
            _warehouse.Id,
            _location.Id);
        _context.AddRange(expiredLot, heldSerial, closedLicensePlate);
        await _context.SaveChangesAsync();
        heldSerial.RecordReceipt(
            _warehouse.Id,
            _location.Id,
            null,
            "receipt-serial",
            quarantine: false,
            _clock.UtcNow.UtcDateTime);
        heldSerial.SetStatus(SerialStatus.Hold, "quality hold", _clock.UtcNow.UtcDateTime);
        closedLicensePlate.Close();
        await _context.SaveChangesAsync();

        await RecordReceiptForKeyAsync(
            new InventoryBalanceKey(
                _warehouse.Id,
                _location.Id,
                _item.Id,
                expiredLot.Id,
                null,
                null,
                null,
                _availableStatus.Id,
                _item.UnitOfMeasure),
            "receipt-expired-lot");
        await RecordReceiptForKeyAsync(
            new InventoryBalanceKey(
                _warehouse.Id,
                _location.Id,
                _item.Id,
                null,
                heldSerial.Id,
                heldSerial.Number,
                null,
                _availableStatus.Id,
                _item.UnitOfMeasure),
            "receipt-held-serial");
        await RecordReceiptForKeyAsync(
            new InventoryBalanceKey(
                _warehouse.Id,
                _location.Id,
                _item.Id,
                null,
                null,
                null,
                closedLicensePlate.Id,
                _availableStatus.Id,
                _item.UnitOfMeasure),
            "receipt-closed-lp");

        var result = await _service.ReserveAsync(CreateRequest("SO-105", 1m));

        result.Status.Should().Be(InventoryReservationStatus.Pending);
        result.AllocatedQuantity.Should().Be(0m);
        (await _context.InventoryBalances.SumAsync(balance => balance.ReservedQuantity)).Should().Be(0m);
    }

    [Fact]
    public async Task ExpiryReleasesActiveAllocationsAndMarksReservationTerminal()
    {
        await SeedReceiptAsync(_location, _availableStatus.Id, 2m, "receipt-4");
        var reserved = await _service.ReserveAsync(
            CreateRequest(
                "SO-104",
                2m,
                expiresAtUtc: _clock.UtcNow.UtcDateTime.AddMinutes(30)));

        var expired = await _service.ExpireAsync(
            _clock.UtcNow.UtcDateTime.AddHours(1),
            "system.expiry",
            "expiry-correlation");

        expired.Should().ContainSingle();
        expired[0].ReservationId.Should().Be(reserved.ReservationId);
        expired[0].Status.Should().Be(InventoryReservationStatus.Expired);
        expired[0].ActiveQuantity.Should().Be(0m);
        expired[0].Events.Should().Contain(eventItem =>
            eventItem.Type == InventoryReservationEventType.Expired &&
            eventItem.Quantity == 2m);
        (await _context.InventoryBalances.SingleAsync()).ReservedQuantity.Should().Be(0m);
        (await _context.InventoryReservationAllocations.SingleAsync()).Status
            .Should().Be(InventoryReservationAllocationStatus.Expired);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task SeedReceiptAsync(
        Location location,
        int statusId,
        decimal quantity,
        string idempotencyKey)
    {
        await _ledger.RecordAsync(
            [
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    new InventoryBalanceKey(
                        _warehouse.Id,
                        location.Id,
                        _item.Id,
                        null,
                        null,
                        null,
                        null,
                        statusId,
                        _item.UnitOfMeasure),
                    quantity,
                    ActorUserId: "receiver-1",
                    IdempotencyKey: idempotencyKey,
                    TransactionGroupId: $"{idempotencyKey}-group")
            ]);
        await _context.SaveChangesAsync();
    }

    private async Task RecordReceiptForKeyAsync(
        InventoryBalanceKey key,
        string idempotencyKey)
    {
        await _ledger.RecordAsync(
            [
                new InventoryLedgerEntryRequest(
                    InventoryTransactionType.Receipt,
                    key,
                    1m,
                    ActorUserId: "receiver-1",
                    IdempotencyKey: idempotencyKey,
                    TransactionGroupId: $"{idempotencyKey}-group")
            ]);
        await _context.SaveChangesAsync();
    }

    private InventoryReservationRequest CreateRequest(
        string demandId,
        decimal requestedQuantity,
        DateTime? expiresAtUtc = null) =>
        new(
            "SalesOrder",
            demandId,
            1,
            _warehouse.Id,
            _item.Id,
            requestedQuantity,
            InventoryReservationMode.Hard,
            Priority: 10,
            ExpiresAtUtc: expiresAtUtc,
            ActorUserId: "allocator-1",
            CorrelationId: $"correlation-{demandId}",
            Reason: "outbound demand");

    private sealed class FixedClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }
}
