using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Forecasting;

namespace Wms.Infrastructure.Tests.Forecasting;

public sealed class ForecastingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _location;
    private readonly InventoryStatus _availableStatus;
    private FixedClock _clock = new(new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero));
    private ForecastingService _service;
    private int _salesOrderLineId = 100;

    public ForecastingServiceTests()
    {
        _connection.Open();
        _context = new WmsDbContext(new DbContextOptionsBuilder<WmsDbContext>()
            .UseSqlite(_connection)
            .Options);
        _context.Database.EnsureCreated();
        _context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        _access.Setup(service => service.AuthorizeAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _access.Setup(service => service.GetScopeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WarehouseAccessScope(true, new HashSet<int>()));
        _audit.Setup(writer => writer.RecordAsync(
                It.IsAny<AuditRecord>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _warehouse = new Warehouse("FORECAST-WH", "Forecast warehouse", timeZone: "UTC");
        _item = new Item("FORECAST-ITEM", "Forecast item", "EA");
        _availableStatus = new InventoryStatus(
            "FORECAST-AVAILABLE",
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
            "FORECAST-BIN",
            "Forecast bin",
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
        balance.Apply(100m, 0m, allowNegativeStock: false);
        _context.AddRange(
            balance,
            new ItemUnitConversion(_item.Id, "CASE", "EA", 12m, resultPrecision: 0),
            new InventoryReplenishmentPolicy(
                _item.Id,
                _warehouse.Id,
                locationId: null,
                minimumQuantity: 0m,
                maximumQuantity: 1_000m,
                safetyStockQuantity: 10m,
                reorderPointQuantity: 20m,
                targetQuantity: 50m,
                InventoryPolicyQuantityBasis.PhysicalAvailable,
                _clock.UtcNow.UtcDateTime.AddYears(-1),
                leadTimeDays: 7));
        _context.SaveChanges();
        _service = CreateService(_clock);
    }

    [Fact]
    public async Task Persists_deduplicates_versions_and_audits_immutable_forecast_overrides()
    {
        var sourceLine = await AddShippedLineAsync(
            new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc),
            quantity: 2m,
            unit: "CASE");
        for (var day = 15; day <= 20; day++)
        {
            if (day == 18)
            {
                continue;
            }

            await AddShippedLineAsync(
                new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc),
                quantity: 2m,
                unit: "CASE");
        }

        await AddCancelledUnshippedLineAsync();
        await AddShippedLineAsync(
            new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc),
            quantity: 50m,
            unit: "CASE");
        await AddCustomerReturnAsync(
            sourceLine.Id,
            new DateTime(2026, 9, 18, 13, 0, 0, DateTimeKind.Utc),
            quantity: 1m);
        AddInventoryTransaction(
            new DateTime(2026, 9, 15, 8, 0, 0, DateTimeKind.Utc),
            before: 0m,
            delta: 100m,
            "opening");
        AddInventoryTransaction(
            new DateTime(2026, 9, 18, 14, 0, 0, DateTimeKind.Utc),
            before: 100m,
            delta: -100m,
            "stockout");
        AddInventoryTransaction(
            new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc),
            before: 0m,
            delta: 100m,
            "restock");
        await _context.SaveChangesAsync();

        var query = new ForecastingRecalculationQuery(
            _warehouse.Id,
            _item.Id,
            ForecastGranularity.Daily,
            HorizonPeriods: 3);
        var first = await _service.RecalculateAsync(query);
        var repeated = await _service.RecalculateAsync(query);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(1, first.Value.RunsCreated);
        Assert.True(repeated.IsSuccess, repeated.Error);
        Assert.Equal(0, repeated.Value.RunsCreated);
        Assert.Equal(1, repeated.Value.RunsReused);
        var run = await _context.ForecastRuns.Include(entity => entity.Points).SingleAsync();
        Assert.Equal(ForecastDataStatus.Ready.ToString(), run.DataStatus);
        Assert.Equal("baseline.v2", run.ModelVersion);
        Assert.Equal(64, run.InputFingerprint.Length);
        Assert.Equal(new DateOnly(2026, 9, 22), run.InputPeriodEnd);
        Assert.Equal(2, run.BacktestEvaluatedPeriods);
        Assert.Contains("missing-periods-filled-with-zero", run.DataQualityFlagsJson);
        var returnAdjustment = run.Points.Single(point =>
            point.PeriodStart == new DateOnly(2026, 9, 18));
        Assert.Equal(12m, returnAdjustment.ActualDemand);
        Assert.True(returnAdjustment.WasStockoutCensored);
        Assert.Equal(8, run.Points.Count(point => point.ActualDemand.HasValue));
        Assert.Equal(3, run.Points.Count(point => point.ForecastQuantity.HasValue));
        Assert.Equal(new DateOnly(2026, 9, 23), run.Points
            .First(point => point.ForecastQuantity.HasValue).PeriodStart);

        var overrideResult = await _service.CreateOverrideAsync(
            run.Id,
            new ForecastOverrideInput(new DateOnly(2026, 9, 23), 44m, "Planner reviewed demand."),
            "planner-1");
        var nextOverride = await _service.CreateOverrideAsync(
            run.Id,
            new ForecastOverrideInput(new DateOnly(2026, 9, 23), 48m, "Updated planning assumption."),
            "planner-1");
        Assert.True(overrideResult.IsSuccess, overrideResult.Error);
        Assert.True(nextOverride.IsSuccess, nextOverride.Error);
        Assert.Equal(1, overrideResult.Value.Version);
        Assert.Equal(2, nextOverride.Value.Version);
        _audit.Verify(writer => writer.RecordAsync(
            It.Is<AuditRecord>(record =>
                record.Action == WmsAuditActions.ForecastOverrideCreated &&
                record.EntityType == WmsAuditEntityTypes.ForecastOverride),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        var details = await _service.GetAsync(run.Id);
        Assert.True(details.IsSuccess, details.Error);
        var overriddenPoint = Assert.Single(details.Value.Points,
            point => point.PeriodStart == new DateOnly(2026, 9, 23));
        Assert.Equal(48m, overriddenPoint.OverrideQuantity);
        Assert.Equal(2, overriddenPoint.OverrideVersion);
        Assert.NotNull(overriddenPoint.ForecastQuantity);

        await AddShippedLineAsync(
            new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc),
            quantity: 1m,
            unit: "CASE");
        var changed = await _service.RecalculateAsync(query);
        Assert.True(changed.IsSuccess, changed.Error);
        Assert.Equal(1, changed.Value.RunsCreated);
        Assert.Equal(2, await _context.ForecastRuns.CountAsync());
        run.ModelVersion += ".changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _context.SaveChangesAsync());

        _access.Setup(service => service.AuthorizeAsync(
                WmsPermissions.InventoryRead,
                999,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(WmsErrors.Forbidden("test.scope", "Denied.")));
        var denied = await _service.SearchAsync(new ForecastingSearchQuery(WarehouseId: 999));
        Assert.True(denied.IsFailure);
        Assert.Equal(ErrorType.Forbidden, denied.FirstError!.Type);
    }

    [Fact]
    public async Task Actual_comparison_uses_only_closed_periods_and_returns_audited_metrics()
    {
        for (var day = 14; day <= 20; day++)
        {
            await AddShippedLineAsync(
                new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc),
                quantity: 2m,
                unit: "CASE");
        }

        var created = await _service.RecalculateAsync(new ForecastingRecalculationQuery(
            _warehouse.Id,
            _item.Id,
            ForecastGranularity.Daily,
            HorizonPeriods: 3));
        Assert.True(created.IsSuccess, created.Error);
        var runId = await _context.ForecastRuns.Select(run => run.Id).SingleAsync();

        await AddShippedLineAsync(
            new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc),
            quantity: 1m,
            unit: "CASE");
        _clock = new FixedClock(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        _service = CreateService(_clock);
        var actuals = await _service.CompareActualsAsync(runId);

        Assert.True(actuals.IsSuccess, actuals.Error);
        Assert.Equal(3, actuals.Value.EvaluatedPeriods);
        Assert.NotNull(actuals.Value.MeanAbsoluteError);
        Assert.Equal("EA", actuals.Value.MeasurementUom);
        var details = await _service.GetAsync(runId);
        Assert.True(details.IsSuccess, details.Error);
        Assert.Equal(true, details.Value.IsStale);
    }

    [Fact]
    public async Task Warehouse_policy_without_lead_time_does_not_use_location_lead_time_for_risk()
    {
        var warehousePolicy = await _context.InventoryReplenishmentPolicies
            .SingleAsync(policy => !policy.LocationId.HasValue);
        warehousePolicy.Update(
            0m,
            1_000m,
            10m,
            20m,
            50m,
            InventoryPolicyQuantityBasis.PhysicalAvailable,
            _clock.UtcNow.UtcDateTime.AddYears(-1),
            leadTimeDays: null);
        _context.InventoryReplenishmentPolicies.Add(new InventoryReplenishmentPolicy(
            _item.Id,
            _warehouse.Id,
            _location.Id,
            minimumQuantity: 0m,
            maximumQuantity: 1_000m,
            safetyStockQuantity: 10m,
            reorderPointQuantity: 20m,
            targetQuantity: 50m,
            InventoryPolicyQuantityBasis.PhysicalAvailable,
            _clock.UtcNow.UtcDateTime.AddYears(-1),
            leadTimeDays: 7));
        for (var day = 15; day <= 18; day++)
        {
            await AddShippedLineAsync(
                new DateTime(2026, 9, day, 12, 0, 0, DateTimeKind.Utc),
                quantity: 2m,
                unit: "EA");
        }

        await _context.SaveChangesAsync();

        var recalculated = await _service.RecalculateAsync(new ForecastingRecalculationQuery(
            _warehouse.Id,
            _item.Id,
            ForecastGranularity.Daily,
            HorizonPeriods: 3));

        Assert.True(recalculated.IsSuccess, recalculated.Error);
        var run = await _context.ForecastRuns.SingleAsync();
        Assert.Equal(ForecastRiskLevel.Unknown.ToString(), run.RiskLevel);
        Assert.Contains("effective-lead-time-policy-missing", run.DataQualityFlagsJson);
    }

    [Fact]
    public async Task Revoked_recalculation_permission_and_cancellation_leave_no_forecast_state()
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(current => current.UserId).Returns("planner-1");
        _access.Setup(service => service.AuthorizeAsync(
                WmsPermissions.ForecastingRecalculate,
                _warehouse.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(WmsErrors.Forbidden("test.revoked", "Permission was revoked.")));
        var authorizedService = new ForecastingService(
            _context,
            _access.Object,
            _audit.Object,
            _clock,
            NullLogger<ForecastingService>.Instance,
            user.Object);

        var denied = await authorizedService.RecalculateAsync(new ForecastingRecalculationQuery(
            _warehouse.Id,
            _item.Id,
            ForecastGranularity.Daily,
            3));
        Assert.True(denied.IsFailure);
        Assert.Equal(ErrorType.Forbidden, denied.FirstError!.Type);
        Assert.Equal(0, await _context.ForecastRuns.CountAsync());

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.RecalculateAsync(new ForecastingRecalculationQuery(
                _warehouse.Id,
                _item.Id,
                ForecastGranularity.Daily,
                3),
                canceled.Token));
        Assert.Equal(0, await _context.ForecastRuns.CountAsync());
    }

    private ForecastingService CreateService(FixedClock clock) =>
        new(
            _context,
            _access.Object,
            _audit.Object,
            clock,
            NullLogger<ForecastingService>.Instance);

    private async Task<ShipmentLine> AddShippedLineAsync(
        DateTime shippedAtUtc,
        decimal quantity,
        string unit)
    {
        var shipment = new Shipment(
            "FS-" + Guid.NewGuid().ToString("N")[..10],
            _warehouse.Id,
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
            plannedShipAtUtc: null,
            externalReference: null,
            createdByUserId: "planner-1");
        _context.Shipments.Add(shipment);
        await _context.SaveChangesAsync();
        _context.Entry(shipment).Property(entity => entity.Status).CurrentValue = ShipmentStatus.Shipped;
        _context.Entry(shipment).Property(entity => entity.ActualShipAtUtc).CurrentValue = shippedAtUtc;
        var line = new ShipmentLine(shipment.Id, _salesOrderLineId++, _item.Id, quantity, unit);
        _context.ShipmentLines.Add(line);
        await _context.SaveChangesAsync();
        return line;
    }

    private async Task AddCancelledUnshippedLineAsync()
    {
        var shipment = new Shipment(
            "FC-" + Guid.NewGuid().ToString("N")[..10],
            _warehouse.Id,
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
            plannedShipAtUtc: null,
            externalReference: null,
            createdByUserId: "planner-1");
        _context.Shipments.Add(shipment);
        await _context.SaveChangesAsync();
        _context.Entry(shipment).Property(entity => entity.Status).CurrentValue = ShipmentStatus.Cancelled;
        _context.ShipmentLines.Add(new ShipmentLine(
            shipment.Id, _salesOrderLineId++, _item.Id, 999m, "CASE"));
        await _context.SaveChangesAsync();
    }

    private async Task AddCustomerReturnAsync(int shipmentLineId, DateTime receivedAtUtc, decimal quantity)
    {
        var authorization = new ReturnAuthorization(
            "FR-" + Guid.NewGuid().ToString("N")[..10],
            _warehouse.Id,
            customerId: null,
            salesOrderId: null,
            shipmentId: null,
            packageId: null,
            _location.Id,
            unplanned: true,
            reason: "Forecast fixture",
            createdByUserId: "planner-1",
            createdAtUtc: receivedAtUtc.AddHours(-1));
        _context.ReturnAuthorizations.Add(authorization);
        await _context.SaveChangesAsync();
        var returnLine = new ReturnLine(
            authorization.Id,
            _item.Id,
            expectedQuantity: quantity,
            salesOrderLineId: null,
            shipmentLineId,
            expectedLotId: null,
            expectedSerialNumberId: null);
        _context.ReturnLines.Add(returnLine);
        await _context.SaveChangesAsync();
        _context.ReturnReceipts.Add(new ReturnReceipt(
            authorization.Id,
            returnLine.Id,
            _item.Id,
            quantity,
            _location.Id,
            _availableStatus.Id,
            lotId: null,
            serialNumberId: null,
            serialNumber: null,
            licensePlateId: null,
            receivedAtUtc));
        await _context.SaveChangesAsync();
    }

    private void AddInventoryTransaction(
        DateTime occurredAtUtc,
        decimal before,
        decimal delta,
        string keySuffix)
    {
        var key = new InventoryBalanceKey(
            _warehouse.Id,
            _location.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure);
        _context.InventoryTransactions.Add(new InventoryTransaction(
            InventoryTransactionType.Adjustment,
            key,
            delta,
            before,
            before + delta,
            reservedQuantityDelta: 0m,
            reservedQuantityBefore: 0m,
            reservedQuantityAfter: 0m,
            actorUserId: "planner-1",
            occurredAtUtc: occurredAtUtc,
            correlationId: "forecast-" + keySuffix,
            idempotencyKey: "forecast-" + keySuffix,
            transactionGroupId: "forecast-" + keySuffix,
            entrySequence: 1));
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
