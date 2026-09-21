using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.ValueAddedServices;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;
using Wms.Infrastructure.ValueAddedServices;

namespace Wms.Infrastructure.Tests.ValueAddedServices;

public sealed class ValueAddedServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly InventoryReservationService _reservations;
    private readonly ValueAddedService _service;
    private readonly Warehouse _warehouse;
    private readonly Location _source;
    private readonly Location _destination;
    private readonly Item _component;
    private readonly Item _finished;
    private readonly InventoryStatus _available;

    public ValueAddedServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();

        _warehouseAccess
            .Setup(access => access.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _warehouseAccess
            .Setup(access => access.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _auditWriter
            .Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWork = new UnitOfWork(_context, _warehouseAccess.Object);
        _ledger = new InventoryLedgerService(
            _unitOfWork,
            _context,
            _warehouseAccess.Object,
            _clock);
        _reservations = new InventoryReservationService(
            _unitOfWork,
            _context,
            _ledger,
            _warehouseAccess.Object,
            _clock,
            NullLogger<InventoryReservationService>.Instance);
        _service = new ValueAddedService(
            _context,
            _unitOfWork,
            _warehouseAccess.Object,
            _reservations,
            _ledger,
            _auditWriter.Object,
            _clock,
            NullLogger<ValueAddedService>.Instance);

        _warehouse = new Warehouse("VAS-WH", "VAS Warehouse");
        _component = new Item("VAS-COMP", "Component", "EA");
        _finished = new Item("VAS-FIN", "Finished Kit", "EA");
        _available = new InventoryStatus(
            "VAS_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _component, _finished, _available);
        _context.SaveChanges();

        _source = new Location("VAS-SOURCE", "VAS Source", _warehouse.Id);
        _destination = new Location("VAS-DEST", "VAS Destination", _warehouse.Id);
        _context.AddRange(_source, _destination);
        _context.SaveChanges();

        var sourceKey = new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            _component.Id,
            null,
            null,
            null,
            null,
            _available.Id,
            _component.UnitOfMeasure);
        var sourceBalance = new InventoryBalance(sourceKey);
        sourceBalance.Apply(10m, 0m, allowNegativeStock: false);
        _context.AddRange(
            sourceBalance,
            new Stock(
                _component.Id,
                _source.Id,
                new Quantity(10m),
                inventoryStatusId: _available.Id));
        _context.SaveChanges();
    }

    [Fact]
    public async Task AssemblyConsumesReservedComponentsProducesTraceableOutputAndReversesIdempotently()
    {
        var kitResult = await _service.CreateKitDefinitionAsync(
            new KitDefinitionInput(
                "VAS-KIT",
                1,
                _finished.Id,
                _finished.UnitOfMeasure,
                _clock.UtcNow.UtcDateTime.AddDays(-1),
                Lines:
                [
                    new KitDefinitionLineInput(
                        1,
                        _component.Id,
                        3m,
                        _component.UnitOfMeasure)
                ]),
            "vas-user");
        kitResult.IsSuccess.Should().BeTrue(kitResult.Error);

        var orderResult = await _service.CreateOrderAsync(
            new ValueAddedServiceOrderInput(
                "vas-order-1",
                ValueAddedServiceType.Assemble,
                _warehouse.Id,
                _source.Id,
                _destination.Id,
                _finished.Id,
                2m,
                kitResult.Value.Id,
                InputSelector: new ValueAddedServiceInputSelector(
                    InventoryStatusId: _available.Id)),
            "vas-user");
        orderResult.IsSuccess.Should().BeTrue(orderResult.Error);

        var released = await _service.ReleaseAsync(orderResult.Value.Id, "vas-user");
        released.IsSuccess.Should().BeTrue(released.Error);
        released.Value.Status.Should().Be(ValueAddedServiceOrderStatus.Released);
        released.Value.WarehouseWorkId.Should().NotBeNull();

        var inputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Input);
        var outputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Output);
        inputLine.ReservationId.Should().NotBeNull();
        inputLine.ReservationAllocationId.Should().NotBeNull();
        inputLine.PlannedQuantity.Should().Be(6m);

        var completed = await _service.CompleteAsync(
            released.Value.Id,
            new ValueAddedServiceCompletionInput(
                "vas-complete-1",
                2m,
                [new ValueAddedServiceOutputInput(outputLine.Id, 2m)]),
            "vas-user");
        completed.IsSuccess.Should().BeTrue(completed.Error);
        completed.Value.Status.Should().Be(ValueAddedServiceOrderStatus.Completed);
        completed.Value.CompletedOutputQuantity.Should().Be(2m);

        var sourceBalance = await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _source.Id && value.ItemId == _component.Id);
        var outputBalance = await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _destination.Id && value.ItemId == _finished.Id);
        sourceBalance.OnHandQuantity.Should().Be(4m);
        sourceBalance.ReservedQuantity.Should().Be(0m);
        outputBalance.OnHandQuantity.Should().Be(2m);

        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedConsumption)).Should().Be(1);
        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedProduction)).Should().Be(1);
        (await _context.ValueAddedServiceTraceLinks.CountAsync()).Should().Be(1);
        (await _context.ValueAddedServiceTraceLinks.SingleAsync()).Quantity.Should().Be(6m);

        var replay = await _service.CompleteAsync(
            released.Value.Id,
            new ValueAddedServiceCompletionInput(
                "vas-complete-1",
                2m,
                [new ValueAddedServiceOutputInput(outputLine.Id, 2m)]),
            "vas-user");
        replay.IsSuccess.Should().BeTrue(replay.Error);
        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedProduction)).Should().Be(1);

        var reversed = await _service.ReverseAsync(
            released.Value.Id,
            new ValueAddedServiceReversalInput("vas-reverse-1", "customer cancellation"),
            "vas-user");
        reversed.IsSuccess.Should().BeTrue(reversed.Error);
        reversed.Value.Status.Should().Be(ValueAddedServiceOrderStatus.Reversed);

        (await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _source.Id && value.ItemId == _component.Id)).OnHandQuantity
            .Should().Be(10m);
        (await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _destination.Id && value.ItemId == _finished.Id)).OnHandQuantity
            .Should().Be(0m);
        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedReversal)).Should().Be(2);
        (await _context.ValueAddedServiceTraceLinks.SingleAsync()).ReversedQuantity.Should().Be(6m);

        var reversalReplay = await _service.ReverseAsync(
            released.Value.Id,
            new ValueAddedServiceReversalInput("vas-reverse-1", "customer cancellation"),
            "vas-user");
        reversalReplay.IsSuccess.Should().BeTrue(reversalReplay.Error);
        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedReversal)).Should().Be(2);
    }

    [Fact]
    public async Task PartialCompletionRecordsYieldScrapAndFinishesWithTheRemainingOutput()
    {
        var (_, order) = await CreateAssemblyOrderAsync(2m);
        var released = await _service.ReleaseAsync(order.Id, "vas-user");
        released.IsSuccess.Should().BeTrue(released.Error);

        var inputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Input);
        var outputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Output);
        var partial = await _service.CompleteAsync(
            order.Id,
            new ValueAddedServiceCompletionInput(
                "vas-partial-1",
                1m,
                [new ValueAddedServiceOutputInput(outputLine.Id, 1m)],
                [new ValueAddedServiceComponentCompletionInput(inputLine.Id, 2m, 1m, "yield variance")]),
            "vas-user");

        partial.IsSuccess.Should().BeTrue(partial.Error);
        partial.Value.Status.Should().Be(ValueAddedServiceOrderStatus.PartiallyCompleted);
        partial.Value.ScrapQuantity.Should().Be(1m);
        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedScrap)).Should().Be(1);
        (await _context.ValueAddedServiceTraceLinks.CountAsync(value =>
            value.Kind == ValueAddedServiceTraceKind.MaterialToScrap)).Should().Be(1);

        var remaining = await _service.CompleteAsync(
            order.Id,
            new ValueAddedServiceCompletionInput(
                "vas-partial-2",
                1m,
                [new ValueAddedServiceOutputInput(outputLine.Id, 1m)]),
            "vas-user");
        remaining.IsSuccess.Should().BeTrue(remaining.Error);
        remaining.Value.Status.Should().Be(ValueAddedServiceOrderStatus.Completed);
        remaining.Value.CompletedOutputQuantity.Should().Be(2m);
        (await _context.InventoryTransactions.CountAsync(value =>
            value.Type == InventoryTransactionType.ValueAddedConsumption)).Should().Be(2);
        (await _context.ValueAddedServiceTraceLinks.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ReleaseRejectsShortageWithoutLeavingAReservationOrWorkItem()
    {
        var (_, order) = await CreateAssemblyOrderAsync(4m);
        var released = await _service.ReleaseAsync(order.Id, "vas-user");

        released.IsFailure.Should().BeTrue();
        released.Error.Should().Contain("shortage");
        (await _context.InventoryReservations.CountAsync()).Should().Be(0);
        (await _context.WarehouseWorks.CountAsync()).Should().Be(0);
        (await _context.ValueAddedServiceOrders.SingleAsync(value => value.Id == order.Id))
            .Status.Should().Be(ValueAddedServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task DisassemblyProducesBidirectionalGenealogyAndReversesWithoutGainOrLoss()
    {
        var parent = new Item("VAS-PARENT", "Parent Kit", "EA");
        _context.Items.Add(parent);
        await _context.SaveChangesAsync();

        var parentKey = new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            parent.Id,
            null,
            null,
            null,
            null,
            _available.Id,
            parent.UnitOfMeasure);
        var parentBalance = new InventoryBalance(parentKey);
        parentBalance.Apply(2m, 0m, allowNegativeStock: false);
        _context.AddRange(
            parentBalance,
            new Stock(parent.Id, _source.Id, new Quantity(2m), inventoryStatusId: _available.Id));

        var kitResult = await _service.CreateKitDefinitionAsync(
            new KitDefinitionInput(
                "VAS-DIS-KIT",
                1,
                parent.Id,
                parent.UnitOfMeasure,
                _clock.UtcNow.UtcDateTime.AddDays(-1),
                Lines:
                [
                    new KitDefinitionLineInput(
                        1,
                        _component.Id,
                        1m,
                        _component.UnitOfMeasure)
                ]),
            "vas-user");
        kitResult.IsSuccess.Should().BeTrue(kitResult.Error);

        var orderResult = await _service.CreateOrderAsync(
            new ValueAddedServiceOrderInput(
                "vas-disassembly-1",
                ValueAddedServiceType.Disassemble,
                _warehouse.Id,
                _source.Id,
                _destination.Id,
                parent.Id,
                2m,
                kitResult.Value.Id,
                InputSelector: new ValueAddedServiceInputSelector(
                    InventoryStatusId: _available.Id)),
            "vas-user");
        orderResult.IsSuccess.Should().BeTrue(orderResult.Error);

        var released = await _service.ReleaseAsync(orderResult.Value.Id, "vas-user");
        released.IsSuccess.Should().BeTrue(released.Error);
        var inputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Input);
        var outputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Output);

        var completed = await _service.CompleteAsync(
            released.Value.Id,
            new ValueAddedServiceCompletionInput(
                "vas-disassembly-complete-1",
                2m,
                [new ValueAddedServiceOutputInput(outputLine.Id, 2m)]),
            "vas-user");
        completed.IsSuccess.Should().BeTrue(completed.Error);
        completed.Value.Status.Should().Be(ValueAddedServiceOrderStatus.Completed);
        completed.Value.Lines.Single(line => line.Id == inputLine.Id).ConsumedQuantity.Should().Be(2m);

        var genealogy = await _service.GetGenealogyAsync(released.Value.Id);
        genealogy.IsSuccess.Should().BeTrue(genealogy.Error);
        genealogy.Value.Should().ContainSingle(row =>
            row.Kind == ValueAddedServiceTraceKind.MaterialToOutput &&
            row.InputLine.ItemId == parent.Id &&
            row.OutputLine!.ItemId == _component.Id &&
            row.Quantity == 2m);

        var reversed = await _service.ReverseAsync(
            released.Value.Id,
            new ValueAddedServiceReversalInput("vas-disassembly-reverse-1", "rework cancelled"),
            "vas-user");
        reversed.IsSuccess.Should().BeTrue(reversed.Error);
        (await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _source.Id && value.ItemId == parent.Id)).OnHandQuantity.Should().Be(2m);
        (await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _destination.Id && value.ItemId == _component.Id)).OnHandQuantity.Should().Be(0m);
        (await _context.ValueAddedServiceTraceLinks.SingleAsync()).ReversedQuantity.Should().Be(2m);
    }

    [Fact]
    public async Task ApprovedSubstitutionReservesAndConsumesTheSubstituteWithTraceableLineage()
    {
        var substitute = new Item("VAS-SUB", "Approved Substitute", "EA");
        _context.Items.Add(substitute);
        await _context.SaveChangesAsync();

        var substituteKey = new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            substitute.Id,
            null,
            null,
            null,
            null,
            _available.Id,
            substitute.UnitOfMeasure);
        var substituteBalance = new InventoryBalance(substituteKey);
        substituteBalance.Apply(12m, 0m, allowNegativeStock: false);
        _context.AddRange(
            substituteBalance,
            new Stock(substitute.Id, _source.Id, new Quantity(12m), inventoryStatusId: _available.Id));
        await _context.SaveChangesAsync();

        var kitResult = await _service.CreateKitDefinitionAsync(
            new KitDefinitionInput(
                "VAS-SUB-KIT",
                1,
                _finished.Id,
                _finished.UnitOfMeasure,
                _clock.UtcNow.UtcDateTime.AddDays(-1),
                Lines:
                [
                    new KitDefinitionLineInput(
                        1,
                        _component.Id,
                        3m,
                        _component.UnitOfMeasure,
                        KitSubstitutionPolicy.ApprovedItems,
                        JsonSerializer.Serialize(new[] { substitute.Id }))
                ]),
            "vas-user");
        kitResult.IsSuccess.Should().BeTrue(kitResult.Error);

        var orderResult = await _service.CreateOrderAsync(
            new ValueAddedServiceOrderInput(
                "vas-substitution-1",
                ValueAddedServiceType.Assemble,
                _warehouse.Id,
                _source.Id,
                _destination.Id,
                _finished.Id,
                4m,
                kitResult.Value.Id,
                InputSelector: new ValueAddedServiceInputSelector(
                    InventoryStatusId: _available.Id)),
            "vas-user");
        orderResult.IsSuccess.Should().BeTrue(orderResult.Error);

        var released = await _service.ReleaseAsync(orderResult.Value.Id, "vas-user");
        released.IsSuccess.Should().BeTrue(released.Error);
        var inputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Input);
        inputLine.IsSubstitution.Should().BeTrue();
        inputLine.ItemId.Should().Be(substitute.Id);
        inputLine.SubstitutedForItemId.Should().Be(_component.Id);
        inputLine.PlannedQuantity.Should().Be(12m);

        var outputLine = released.Value.Lines.Single(line => line.Kind == ValueAddedServiceLineKind.Output);
        var completed = await _service.CompleteAsync(
            released.Value.Id,
            new ValueAddedServiceCompletionInput(
                "vas-substitution-complete-1",
                4m,
                [new ValueAddedServiceOutputInput(outputLine.Id, 4m)]),
            "vas-user");
        completed.IsSuccess.Should().BeTrue(completed.Error);
        (await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _source.Id && value.ItemId == substitute.Id)).OnHandQuantity.Should().Be(0m);
        (await _context.InventoryBalances.SingleAsync(value =>
            value.LocationId == _source.Id && value.ItemId == _component.Id)).OnHandQuantity.Should().Be(10m);
        (await _context.ValueAddedServiceTraceLinks.SingleAsync()).InputLineId.Should().Be(inputLine.Id);
    }

    private async Task<(KitDefinitionDto Kit, ValueAddedServiceOrderDto Order)> CreateAssemblyOrderAsync(
        decimal quantity)
    {
        var kitResult = await _service.CreateKitDefinitionAsync(
            new KitDefinitionInput(
                "VAS-KIT",
                1,
                _finished.Id,
                _finished.UnitOfMeasure,
                _clock.UtcNow.UtcDateTime.AddDays(-1),
                Lines:
                [
                    new KitDefinitionLineInput(
                        1,
                        _component.Id,
                        3m,
                        _component.UnitOfMeasure)
                ]),
            "vas-user");
        kitResult.IsSuccess.Should().BeTrue(kitResult.Error);

        var orderResult = await _service.CreateOrderAsync(
            new ValueAddedServiceOrderInput(
                $"vas-order-{quantity}",
                ValueAddedServiceType.Assemble,
                _warehouse.Id,
                _source.Id,
                _destination.Id,
                _finished.Id,
                quantity,
                kitResult.Value.Id,
                InputSelector: new ValueAddedServiceInputSelector(
                    InventoryStatusId: _available.Id)),
            "vas-user");
        orderResult.IsSuccess.Should().BeTrue(orderResult.Error);
        return (kitResult.Value, orderResult.Value);
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
