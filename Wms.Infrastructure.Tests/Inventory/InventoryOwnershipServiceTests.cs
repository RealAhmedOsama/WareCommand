using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using Wms.Infrastructure.Repositories;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class InventoryOwnershipServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _warehouseAccess = new();
    private readonly Mock<IAuditWriter> _auditWriter = new();
    private readonly WmsDbContext _context;
    private readonly UnitOfWork _unitOfWork;
    private readonly InventoryLedgerService _ledger;
    private readonly InventoryOwnershipService _service;
    private readonly InventoryOwnershipReportService _reportService;
    private readonly RecordingLogger<InventoryOwnershipReportService> _reportLogger = new();
    private readonly Warehouse _warehouse;
    private readonly Location _location;
    private readonly Item _item;
    private readonly InventoryStatus _status;
    private readonly InventoryOwner _externalOwner;

    public InventoryOwnershipServiceTests()
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
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)));
        _service = new InventoryOwnershipService(
            _context,
            _unitOfWork,
            _warehouseAccess.Object,
            _ledger,
            _auditWriter.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<InventoryOwnershipService>.Instance);
        _reportService = new InventoryOwnershipReportService(
            _context,
            _warehouseAccess.Object,
            new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero)),
            _reportLogger);

        _warehouse = new Warehouse("OWN-WH", "Ownership warehouse");
        _item = new Item("OWN-ITEM", "Ownership item", "EA");
        _status = new InventoryStatus(
            "OWN_AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _status);
        _context.SaveChanges();
        _location = new Location("OWN-BIN", "Ownership bin", _warehouse.Id);
        _externalOwner = new InventoryOwner(
            "EXT-OWN-1",
            InventoryOwnerKind.ExternalOwner,
            "External owner",
            externalOwnerReference: "EXT-OWN-1");
        _context.AddRange(_location, _externalOwner);
        _context.SaveChanges();

        var companyKey = new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _status.Id,
            _item.UnitOfMeasure);
        var balance = new InventoryBalance(companyKey);
        balance.Apply(5m, 0m, allowNegativeStock: false);
        _context.AddRange(
            balance,
            new Stock(
                _item.Id,
                _location.Id,
                new Quantity(5m),
                inventoryStatusId: _status.Id));
        _context.SaveChanges();
    }

    [Fact]
    public async Task TransferMovesOwnerQuantityWithTwoLedgerLegsAndReplaysIdempotently()
    {
        var input = new InventoryOwnershipTransferInput(
            "ownership-transfer-1",
            _warehouse.Id,
            _item.Id,
            3m,
            _item.UnitOfMeasure,
            _location.Id,
            _status.Id,
            InventoryOwnerKind.CompanyOwned,
            null,
            InventoryOwnershipDimension.CompanyOwnerCode,
            InventoryOwnerKind.ExternalOwner,
            _externalOwner.Id,
            _externalOwner.OwnerCode,
            Reason: "approved owner transfer");

        var result = await _service.TransferAsync(input, "owner-manager");
        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Status.Should().Be(InventoryOwnershipTransferStatus.Completed);

        var sourceStock = await _context.Stock.SingleAsync(stock =>
            stock.OwnerKind == InventoryOwnerKind.CompanyOwned);
        var destinationStock = await _context.Stock.SingleAsync(stock =>
            stock.OwnerKind == InventoryOwnerKind.ExternalOwner);
        sourceStock.QuantityAvailable.Value.Should().Be(2m);
        destinationStock.QuantityAvailable.Value.Should().Be(3m);

        var transferTransactions = await _context.InventoryTransactions
            .Where(transaction => transaction.Type == InventoryTransactionType.OwnershipTransfer)
            .ToListAsync();
        transferTransactions.Should().HaveCount(2);
        transferTransactions.Sum(transaction => transaction.QuantityDelta).Should().Be(0m);
        (await _context.InventoryOwnershipTransfers.CountAsync()).Should().Be(1);

        var report = await _reportService.QueryAsync(
            new InventoryOwnershipReportQuery(WarehouseId: _warehouse.Id));
        report.IsSuccess.Should().BeTrue(
            $"{report.Error} {string.Join(" | ", _reportLogger.Exceptions.Select(exception => exception.ToString()))}");
        report.Value.Balances.Should().Contain(row =>
            row.OwnerKind == InventoryOwnerKind.CompanyOwned &&
            row.AvailableQuantity == 2m);
        report.Value.Balances.Should().Contain(row =>
            row.OwnerKind == InventoryOwnerKind.ExternalOwner &&
            row.InventoryOwnerId == _externalOwner.Id &&
            row.AvailableQuantity == 3m);
        var ownershipUsage = report.Value.Usage
            .Where(row => row.TransactionType == InventoryTransactionType.OwnershipTransfer)
            .ToArray();
        ownershipUsage.Should().HaveCount(2);
        ownershipUsage.Sum(row => row.TransactionCount).Should().Be(2);
        ownershipUsage.Sum(row => row.QuantityDelta).Should().Be(0m);
        report.Value.Statement.Should().HaveCount(2);

        var replay = await _service.TransferAsync(input, "owner-manager");
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Id.Should().Be(result.Value.Id);
        (await _context.InventoryTransactions.CountAsync(
            transaction => transaction.Type == InventoryTransactionType.OwnershipTransfer))
            .Should().Be(2);

        var conflict = await _service.TransferAsync(
            input with { Quantity = 4m },
            "owner-manager");
        conflict.IsFailure.Should().BeTrue();
        conflict.ErrorCode.Should().Be("inventory.ownership_transfer_idempotency_conflict");
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

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Exception> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                Exceptions.Add(exception);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
