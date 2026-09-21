using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Putaway;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Putaway;

namespace Wms.Infrastructure.Tests.Putaway;

public sealed class PutawayRuleServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly WmsDbContext _context;
    private readonly Mock<IWarehouseAccessService> _access = new();
    private readonly Mock<IAuditWriter> _audit = new();
    private readonly PutawayRuleService _service;
    private readonly Warehouse _warehouse;
    private readonly Item _item;
    private readonly Location _fixedLocation;

    public PutawayRuleServiceTests()
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

        _service = new PutawayRuleService(
            _context,
            _access.Object,
            _audit.Object,
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PutawayRuleService>());

        _warehouse = new Warehouse("RULE-WH", "Rule Warehouse");
        _item = new Item("RULE-ITEM", "Rule Item", "EA");
        _context.AddRange(_warehouse, _item);
        _context.SaveChanges();

        _fixedLocation = new Location(
            "RULE-FIXED",
            "Fixed storage",
            _warehouse.Id,
            type: LocationType.Storage,
            priority: 10,
            maxUnits: 5);
        _context.Add(_fixedLocation);
        _context.SaveChanges();
    }

    [Fact]
    public async Task FixedRuleProducesDeterministicSuggestion()
    {
        var created = await _service.CreateAsync(
            new PutawayRuleInput(
                _warehouse.Id,
                "fixed-rule",
                "Fixed rule",
                PutawayRuleStrategy.FixedLocation,
                100,
                DateTime.UtcNow.AddHours(-1),
                FixedLocationId: _fixedLocation.Id),
            "manager-1");

        var suggestion = await _service.SuggestAsync(
            new PutawaySuggestionInput(
                _warehouse.Id,
                _item.Id,
                4m,
                SourceProcess: "RECEIPT"),
            "receiver-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        suggestion.IsSuccess.Should().BeTrue(suggestion.Error);
        suggestion.Value.HasValidSuggestion.Should().BeTrue();
        suggestion.Value.Suggestions.Should().ContainSingle();
        suggestion.Value.Suggestions[0].LocationId.Should().Be(_fixedLocation.Id);
        suggestion.Value.Suggestions[0].Explanation.Should().Contain("Fixed");
    }

    [Fact]
    public async Task CapacityViolationIsRejectedAndReturnsNoMatch()
    {
        var constrained = new Location(
            "RULE-FULL",
            "Full storage",
            _warehouse.Id,
            type: LocationType.Storage,
            maxUnits: 5);
        _context.Add(constrained);
        await _context.SaveChangesAsync();

        var created = await _service.CreateAsync(
            new PutawayRuleInput(
                _warehouse.Id,
                "capacity-rule",
                "Capacity rule",
                PutawayRuleStrategy.CapacityAware,
                100,
                DateTime.UtcNow.AddHours(-1),
                TargetLocationType: LocationType.Storage),
            "manager-1");

        var suggestion = await _service.SuggestAsync(
            new PutawaySuggestionInput(_warehouse.Id, _item.Id, 6m),
            "receiver-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        suggestion.IsSuccess.Should().BeTrue(suggestion.Error);
        suggestion.Value.HasValidSuggestion.Should().BeFalse();
        suggestion.Value.NoMatchReason.Should().NotBeNullOrWhiteSpace();
        suggestion.Value.Rejections.Should().Contain(rejection =>
            rejection.Code == "location.capacity_units_exceeded");
    }

    [Fact]
    public async Task SimulationRulesRemainExcludedFromOperationalSuggestions()
    {
        var created = await _service.CreateAsync(
            new PutawayRuleInput(
                _warehouse.Id,
                "simulation-rule",
                "Simulation rule",
                PutawayRuleStrategy.FixedLocation,
                100,
                DateTime.UtcNow.AddHours(-1),
                FixedLocationId: _fixedLocation.Id,
                IsSimulation: true),
            "manager-1");

        var operational = await _service.SuggestAsync(
            new PutawaySuggestionInput(_warehouse.Id, _item.Id, 1m),
            "receiver-1");
        var simulation = await _service.SuggestAsync(
            new PutawaySuggestionInput(
                _warehouse.Id,
                _item.Id,
                1m,
                Simulation: true),
            "manager-1");

        created.IsSuccess.Should().BeTrue(created.Error);
        operational.Value.HasValidSuggestion.Should().BeFalse();
        simulation.Value.HasValidSuggestion.Should().BeTrue();
        simulation.Value.Suggestions[0].RuleCode.Should().Be("SIMULATION-RULE");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
