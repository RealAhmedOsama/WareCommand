using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Putaway;

public sealed record PutawayRuleQuery(
    int? WarehouseId = null,
    bool IncludeInactive = false,
    bool IncludeSimulation = false,
    int Page = 1,
    int PageSize = 50);

public sealed record PutawayRuleInput(
    int WarehouseId,
    string Code,
    string Name,
    PutawayRuleStrategy Strategy,
    int Priority,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc = null,
    int? ItemId = null,
    string? ItemCategory = null,
    int? SupplierId = null,
    string? PackageType = null,
    LicensePlateType? LicensePlateType = null,
    int? InventoryStatusId = null,
    LotStatus? LotStatus = null,
    decimal? MinimumTemperatureCelsius = null,
    decimal? MaximumTemperatureCelsius = null,
    string? HazardClass = null,
    string? StorageProfile = null,
    string? SourceProcess = null,
    int? FixedLocationId = null,
    LocationType? TargetLocationType = null,
    string? TargetStorageProfile = null,
    int? FallbackLocationId = null,
    bool IsSimulation = false,
    bool IsActive = true,
    string? Notes = null);

public sealed record PutawayRuleDto(
    int Id,
    int WarehouseId,
    string Code,
    string Name,
    PutawayRuleStrategy Strategy,
    int Priority,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveToUtc,
    int? ItemId,
    string? ItemCategory,
    int? SupplierId,
    string? PackageType,
    LicensePlateType? LicensePlateType,
    int? InventoryStatusId,
    LotStatus? LotStatus,
    decimal? MinimumTemperatureCelsius,
    decimal? MaximumTemperatureCelsius,
    string? HazardClass,
    string? StorageProfile,
    string? SourceProcess,
    int? FixedLocationId,
    LocationType? TargetLocationType,
    string? TargetStorageProfile,
    int? FallbackLocationId,
    bool IsSimulation,
    bool IsActive,
    string? Notes,
    long Revision);

public sealed record PutawaySuggestionInput(
    int WarehouseId,
    int ItemId,
    decimal Quantity,
    int? LotId = null,
    int? InventoryStatusId = null,
    int? LicensePlateId = null,
    LicensePlateType? LicensePlateType = null,
    int? SupplierId = null,
    string? PackageType = null,
    string? SourceProcess = null,
    DateTime? AtUtc = null,
    bool Simulation = false);

public sealed record PutawayLocationSuggestionDto(
    int LocationId,
    string LocationCode,
    string LocationName,
    LocationType LocationType,
    int RuleId,
    string RuleCode,
    PutawayRuleStrategy Strategy,
    int RulePriority,
    decimal Score,
    string Explanation);

public sealed record PutawayLocationRejectionDto(
    int? LocationId,
    string? LocationCode,
    int? RuleId,
    string? RuleCode,
    string Code,
    string Reason);

public sealed record PutawaySuggestionResultDto(
    IReadOnlyList<PutawayLocationSuggestionDto> Suggestions,
    IReadOnlyList<PutawayLocationRejectionDto> Rejections,
    bool Simulation,
    bool HasValidSuggestion,
    string? NoMatchReason);

public interface IPutawayRuleService
{
    Task<Result<PutawayRulePageDto>> ListAsync(
        PutawayRuleQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<PutawayRuleDto>> GetAsync(
        int ruleId,
        CancellationToken cancellationToken = default);

    Task<Result<PutawayRuleDto>> CreateAsync(
        PutawayRuleInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PutawayRuleDto>> UpdateAsync(
        int ruleId,
        PutawayRuleInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PutawayRuleDto>> SetActiveAsync(
        int ruleId,
        bool active,
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result<PutawaySuggestionResultDto>> SuggestAsync(
        PutawaySuggestionInput input,
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed record PutawayRulePageDto(
    IReadOnlyList<PutawayRuleDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
