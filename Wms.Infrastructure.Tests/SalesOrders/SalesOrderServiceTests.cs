using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Customers;
using Wms.Application.Identity;
using Wms.Application.SalesOrders;
using Wms.Application.Units;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.SalesOrders;

namespace Wms.Infrastructure.Tests.SalesOrders;

public sealed class SalesOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<ICustomerManagementService> _customers = new();
    private readonly Mock<IItemQuantityConversionService> _quantityConversion = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly WmsDbContext _context;
    private readonly SalesOrderService _service;
    private readonly Warehouse _warehouse;
    private readonly Customer _customer;
    private readonly Item _item;

    public SalesOrderServiceTests()
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

        _warehouse = new Warehouse("OUT-WH", "Outbound Warehouse");
        _customer = new Customer("OUT-CUST", "Outbound Customer", contactEmail: "customer@example.com");
        _item = new Item("OUT-ITEM", "Outbound Item", "EA");
        _context.AddRange(_warehouse, _customer, _item);
        _context.SaveChanges();

        _customers
            .Setup(service => service.GetDocumentSnapshotAsync(
                It.IsAny<CustomerDocumentSnapshotQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerDocumentSnapshotQuery query, CancellationToken _) =>
                Result.Success(new CustomerDocumentSnapshot(
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
                    null,
                    null,
                    "FAST",
                    "NEXT",
                    50,
                    "BOX",
                    "LABEL",
                    true)));
        _quantityConversion
            .Setup(service => service.ConvertToBaseAsync(
                It.IsAny<int>(),
                It.IsAny<decimal>(),
                It.IsAny<string?>(),
                It.IsAny<QuantityRoundingMode?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, decimal quantity, string? unit, QuantityRoundingMode? _, CancellationToken _) =>
                Result.Success(new QuantityConversionResult(
                    quantity,
                    unit ?? "EA",
                    quantity,
                    "EA",
                    1m,
                    0,
                    QuantityRoundingMode.Reject,
                    0m,
                    "EA -> EA",
                    string.Empty)));

        _service = new SalesOrderService(
            _context,
            _access.Object,
            _customers.Object,
            _quantityConversion.Object,
            _audit.Object,
            _clock,
            NullLogger<SalesOrderService>.Instance);
    }

    [Fact]
    public async Task CreateConfirmAndListAsync_PersistsCustomerAndQuantitySnapshots()
    {
        var created = await _service.CreateAsync(
            new SalesOrderInput(
                _warehouse.Id,
                _customer.Id,
                OrderDate: new DateOnly(2026, 9, 21),
                ExternalReference: "channel-100",
                SourceType: "channel",
                Lines: [new SalesOrderLineInput("out-item", 5m)]),
            "user-1");

        var confirmed = await _service.ConfirmAsync(created.Value.Id, "user-1");
        var listed = await _service.ListAsync(new SalesOrderListQuery(_warehouse.Id));

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value.DocumentNumber.Should().Be("SO-OUT-WH-000001");
        created.Value.Status.Should().Be(SalesOrderStatus.Draft);
        created.Value.Lines.Should().ContainSingle(line =>
            line.OrderedBaseQuantity == 5m && line.BaseUnitOfMeasure == "EA");
        confirmed.IsSuccess.Should().BeTrue(confirmed.Error);
        confirmed.Value.Status.Should().Be(SalesOrderStatus.Confirmed);
        confirmed.Value.CustomerCode.Should().Be("OUT-CUST");
        listed.IsSuccess.Should().BeTrue(listed.Error);
        listed.Value.TotalCount.Should().Be(1);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.SalesOrderConfirmed),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DuplicateExternalReferenceAndInactiveCommandsAreRejected()
    {
        var first = await _service.CreateAsync(
            new SalesOrderInput(
                _warehouse.Id,
                _customer.Id,
                ExternalReference: "DUP-1",
                Lines: [new SalesOrderLineInput("OUT-ITEM", 1m)]),
            "user-1");
        var duplicate = await _service.CreateAsync(
            new SalesOrderInput(
                _warehouse.Id,
                _customer.Id,
                ExternalReference: "dup-1",
                Lines: [new SalesOrderLineInput("OUT-ITEM", 1m)]),
            "user-1");
        var closed = await _service.CloseAsync(first.Value.Id, "user-1");

        first.IsSuccess.Should().BeTrue(first.Error);
        duplicate.ErrorCode.Should().Be("sales_order.external_reference_conflict");
        closed.ErrorCode.Should().Be("sales_order.command_invalid");
    }

    [Fact]
    public async Task HoldReleaseAndCancelRemainExplicitAndAudited()
    {
        var created = await _service.CreateAsync(
            new SalesOrderInput(
                _warehouse.Id,
                _customer.Id,
                Lines: [new SalesOrderLineInput("OUT-ITEM", 2m)]),
            "user-1");

        var held = await _service.HoldAsync(created.Value.Id, "manual review", "user-1");
        var released = await _service.ReleaseHoldAsync(created.Value.Id, "user-1");
        var cancelled = await _service.CancelAsync(created.Value.Id, "user-1");

        held.IsSuccess.Should().BeTrue(held.Error);
        held.Value.Status.Should().Be(SalesOrderStatus.Held);
        released.Value.Status.Should().Be(SalesOrderStatus.Draft);
        cancelled.Value.Status.Should().Be(SalesOrderStatus.Cancelled);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record => record.Action == WmsAuditActions.SalesOrderHeld),
            It.IsAny<CancellationToken>()), Times.Once);
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
