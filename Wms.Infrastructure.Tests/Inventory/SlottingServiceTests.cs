using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.WarehouseWork;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Inventory;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Tests.Inventory;

public sealed class SlottingServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly Mock<IWarehouseWorkService> _work = new();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly SlottingService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly InventoryStatus _availableStatus;
    private readonly Location _source;
    private readonly Location _target;
    private readonly Location _capacityBlocked;

    public SlottingServiceTests()
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

        _warehouse = new Warehouse("SLOT-WH", "Slotting warehouse");
        _item = new Item("SLOT-ITEM", "Slotting item", "EA");
        _availableStatus = new InventoryStatus(
            "SLOT-AVAILABLE",
            "Available",
            "متاح",
            isAvailable: true,
            isAllocatable: true,
            isPickable: true,
            isShippable: true,
            isCountable: true);
        _context.AddRange(_warehouse, _item, _availableStatus);
        _context.SaveChanges();

        _source = new Location(
            "SLOT-SOURCE",
            "Slotting source",
            _warehouse.Id,
            type: LocationType.Bin,
            priority: 50,
            isReceivable: false,
            maxUnits: 1_000);
        _target = new Location(
            "SLOT-TARGET",
            "Slotting target",
            _warehouse.Id,
            type: LocationType.PickFace,
            priority: 1,
            isReceivable: false,
            maxUnits: 100);
        _capacityBlocked = new Location(
            "SLOT-BLOCKED",
            "Capacity blocked",
            _warehouse.Id,
            type: LocationType.Bin,
            priority: 2,
            isReceivable: false,
            maxUnits: 5);
        _context.AddRange(_source, _target, _capacityBlocked);
        _context.SaveChanges();

        var balance = new InventoryBalance(new InventoryBalanceKey(
            _warehouse.Id,
            _source.Id,
            _item.Id,
            null,
            null,
            null,
            null,
            _availableStatus.Id,
            _item.UnitOfMeasure));
        balance.Apply(10m, 0m, allowNegativeStock: false);
        _context.Add(balance);
        _context.SaveChanges();

        _service = new SlottingService(
            _context,
            _access.Object,
            _audit.Object,
            _clock,
            _work.Object,
            NullLogger<SlottingService>.Instance);
    }

    [Fact]
    public async Task AnalysisRanksTheValidTargetAndRejectsCapacityViolations()
    {
        var policy = await SavePolicyAsync();

        var result = await _service.AnalyzeAsync(
            new SlottingAnalysisQuery(
                _warehouse.Id,
                policy.Id,
                AsOfUtc: _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner");

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.RecommendationsCreated.Should().Be(1);
        result.Value.Recommendations.Should().ContainSingle();
        result.Value.Recommendations[0].TargetLocationId.Should().Be(_target.Id);
        result.Value.Recommendations[0].FactorSnapshotJson.Should().Contain("scoreIsAnEstimate");
        result.Value.Recommendations[0].SourcePeriodFromUtc
            .Should().Be(_clock.UtcNow.UtcDateTime.AddDays(-30));
        (await _context.SlottingRecommendations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AnalysisIsIdempotentAndDryRunDoesNotWrite()
    {
        var policy = await SavePolicyAsync();
        var query = new SlottingAnalysisQuery(
            _warehouse.Id,
            policy.Id,
            AsOfUtc: _clock.UtcNow.UtcDateTime,
            Limit: 20);

        var first = await _service.AnalyzeAsync(query, "planner");
        var replay = await _service.AnalyzeAsync(query, "planner");
        var dryRun = await _service.AnalyzeAsync(query with { DryRun = true }, "planner");

        first.IsSuccess.Should().BeTrue(first.Error);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.RecommendationsCreated.Should().Be(0);
        replay.Value.RecommendationsReused.Should().Be(1);
        dryRun.IsSuccess.Should().BeTrue(dryRun.Error);
        dryRun.Value.DryRun.Should().BeTrue();
        dryRun.Value.Recommendations.Should().ContainSingle(row => row.Id == 0);
        (await _context.SlottingRecommendations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ApprovalCreatesReplenishmentWorkExactlyOnce()
    {
        var policy = await SavePolicyAsync();
        var analysis = await _service.AnalyzeAsync(
            new SlottingAnalysisQuery(
                _warehouse.Id,
                policy.Id,
                AsOfUtc: _clock.UtcNow.UtcDateTime,
                Limit: 20),
            "planner");
        var recommendationId = analysis.Value.Recommendations.Single().Id;
        var persistedWork = new WarehouseWorkEntity(
            "WORK-SLOT-42",
            $"slotting:{recommendationId}",
            WarehouseWorkType.Replenishment,
            _warehouse.Id,
            "SlottingRecommendation",
            recommendationId.ToString(CultureInfo.InvariantCulture));
        persistedWork.MakeAvailable(_clock.UtcNow.UtcDateTime);
        _context.WarehouseWorks.Add(persistedWork);
        await _context.SaveChangesAsync();
        _work
            .Setup(service => service.CreateAsync(
                It.IsAny<WarehouseWorkInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(CreateWorkDto(persistedWork.Id)));

        var approved = await _service.ApproveAsync(recommendationId, "supervisor");
        var replay = await _service.ApproveAsync(recommendationId, "supervisor");

        approved.IsSuccess.Should().BeTrue(approved.Error);
        approved.Value.Status.Should().Be(SlottingRecommendationStatus.WorkCreated);
        approved.Value.WorkId.Should().Be(persistedWork.Id);
        replay.IsSuccess.Should().BeTrue(replay.Error);
        replay.Value.Status.Should().Be(SlottingRecommendationStatus.WorkCreated);
        _work.Verify(
            service => service.CreateAsync(
                It.IsAny<WarehouseWorkInput>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        (await _context.SlottingRecommendations.SingleAsync()).Status
            .Should().Be(SlottingRecommendationStatus.WorkCreated);
    }

    private async Task<SlottingPolicyDto> SavePolicyAsync()
    {
        var result = await _service.SavePolicyAsync(
            null,
            new SlottingPolicyInput(
                _warehouse.Id,
                "SLOT-DEFAULT",
                "Default slotting",
                30,
                VelocityWeight: 0.25m,
                TravelWeight: 0.45m,
                SpaceWeight: 0.2m,
                ReplenishmentWeight: 0.05m,
                AffinityWeight: 0.05m,
                MaxRecommendationsPerItem: 1,
                RecommendationExpiryDays: 7,
                EffectiveFromUtc: _clock.UtcNow.UtcDateTime.AddDays(-1),
                AllowedLocationTypes: "PickFace,Bin"),
            "planner");
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value;
    }

    private WarehouseWorkDto CreateWorkDto(int workId) => new(
        workId,
        "WORK-SLOT-42",
        "slotting:42",
        WarehouseWorkType.Replenishment,
        _warehouse.Id,
        "SlottingRecommendation",
        "1",
        null,
        "SLOTTING",
        40,
        null,
        null,
        "Approved slotting move",
        WarehouseWorkStatus.Available,
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
        null,
        false,
        null,
        1,
        [],
        false,
        false);

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
