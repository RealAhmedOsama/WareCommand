using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Putaway;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Putaway;

public sealed class PutawayRuleService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    ILogger<PutawayRuleService> logger) : IPutawayRuleService
{
    private const int MaximumPageSize = 200;
    private const int MaximumRejections = 2_000;

    public async Task<Result<PutawayRulePageDto>> ListAsync(
        PutawayRuleQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PutawayRulePageDto>();
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var rules = context.PutawayRules.AsNoTracking().AsQueryable();
        if (!scope.HasGlobalAccess)
        {
            rules = rules.Where(rule => scope.WarehouseIds.Contains(rule.WarehouseId));
        }

        if (query.WarehouseId.HasValue)
        {
            rules = rules.Where(rule => rule.WarehouseId == query.WarehouseId.Value);
        }

        if (!query.IncludeInactive)
        {
            rules = rules.Where(rule => rule.IsActive);
        }

        if (!query.IncludeSimulation)
        {
            rules = rules.Where(rule => !rule.IsSimulation);
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var totalCount = await rules.CountAsync(cancellationToken);
        var rows = await rules
            .OrderByDescending(rule => rule.Priority)
            .ThenBy(rule => rule.Code)
            .ThenBy(rule => rule.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new PutawayRulePageDto(
            rows.Select(Map).ToArray(),
            page,
            pageSize,
            totalCount,
            totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    public async Task<Result<PutawayRuleDto>> GetAsync(
        int ruleId,
        CancellationToken cancellationToken = default)
    {
        var rule = await context.PutawayRules.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == ruleId, cancellationToken);
        if (rule is null)
        {
            return Result.Failure<PutawayRuleDto>(WmsErrors.NotFound(
                "putaway_rule.not_found",
                "The putaway rule was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsRead,
            rule.WarehouseId,
            cancellationToken);
        return authorization.IsFailure
            ? authorization.ToFailure<PutawayRuleDto>()
            : Result.Success(Map(rule));
    }

    public async Task<Result<PutawayRuleDto>> CreateAsync(
        PutawayRuleInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PutawayRuleDto>();
        }

        try
        {
            var validation = await ValidateReferencesAsync(input, cancellationToken);
            if (validation.IsFailure)
            {
                return validation.ToFailure<PutawayRuleDto>();
            }

            var code = input.Code.Trim().ToUpperInvariant();
            if (await context.PutawayRules.AnyAsync(
                    rule => rule.WarehouseId == input.WarehouseId && rule.Code == code,
                    cancellationToken))
            {
                return Result.Failure<PutawayRuleDto>(WmsErrors.Conflict(
                    "putaway_rule.code_conflict",
                    $"Putaway rule code '{code}' already exists in this warehouse."));
            }

            var rule = new PutawayRule(
                input.WarehouseId,
                code,
                input.Name,
                input.Strategy,
                input.Priority,
                input.EffectiveFromUtc,
                input.EffectiveToUtc,
                input.ItemId,
                input.ItemCategory,
                input.SupplierId,
                input.PackageType,
                input.LicensePlateType,
                input.InventoryStatusId,
                input.LotStatus,
                input.MinimumTemperatureCelsius,
                input.MaximumTemperatureCelsius,
                input.HazardClass,
                input.StorageProfile,
                input.SourceProcess,
                input.FixedLocationId,
                input.TargetLocationType,
                input.TargetStorageProfile,
                input.FallbackLocationId,
                input.IsSimulation,
                input.IsActive,
                input.Notes);
            context.PutawayRules.Add(rule);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PutawayRuleCreated,
                    WmsAuditEntityTypes.PutawayRule,
                    rule.Code,
                    rule.WarehouseId,
                    After: ToAuditValues(rule),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(rule));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PutawayRuleDto>(WmsErrors.Validation(
                "putaway_rule.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create putaway rule {RuleCode}", input.Code);
            return Result.Failure<PutawayRuleDto>(WmsErrors.FromException(
                exception,
                "putaway_rule.create_failed",
                "The putaway rule could not be created."));
        }
    }

    public async Task<Result<PutawayRuleDto>> UpdateAsync(
        int ruleId,
        PutawayRuleInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var rule = await context.PutawayRules.SingleOrDefaultAsync(
            candidate => candidate.Id == ruleId,
            cancellationToken);
        if (rule is null)
        {
            return Result.Failure<PutawayRuleDto>(WmsErrors.NotFound(
                "putaway_rule.not_found",
                "The putaway rule was not found."));
        }

        if (rule.WarehouseId != input.WarehouseId)
        {
            return Result.Failure<PutawayRuleDto>(WmsErrors.Validation(
                "putaway_rule.warehouse_immutable",
                "A putaway rule cannot be moved to another warehouse."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            rule.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PutawayRuleDto>();
        }

        try
        {
            var validation = await ValidateReferencesAsync(input, cancellationToken);
            if (validation.IsFailure)
            {
                return validation.ToFailure<PutawayRuleDto>();
            }

            rule.Update(
                input.Name,
                input.Strategy,
                input.Priority,
                input.EffectiveFromUtc,
                input.EffectiveToUtc,
                input.ItemId,
                input.ItemCategory,
                input.SupplierId,
                input.PackageType,
                input.LicensePlateType,
                input.InventoryStatusId,
                input.LotStatus,
                input.MinimumTemperatureCelsius,
                input.MaximumTemperatureCelsius,
                input.HazardClass,
                input.StorageProfile,
                input.SourceProcess,
                input.FixedLocationId,
                input.TargetLocationType,
                input.TargetStorageProfile,
                input.FallbackLocationId,
                input.IsSimulation,
                input.Notes);
            if (rule.IsActive != input.IsActive)
            {
                rule.SetActive(input.IsActive);
            }

            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.PutawayRuleUpdated,
                    WmsAuditEntityTypes.PutawayRule,
                    rule.Code,
                    rule.WarehouseId,
                    After: ToAuditValues(rule),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(rule));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<PutawayRuleDto>(WmsErrors.Validation(
                "putaway_rule.definition_invalid",
                exception.Message));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not update putaway rule {RuleId}", ruleId);
            return Result.Failure<PutawayRuleDto>(WmsErrors.FromException(
                exception,
                "putaway_rule.update_failed",
                "The putaway rule could not be updated."));
        }
    }

    public async Task<Result<PutawayRuleDto>> SetActiveAsync(
        int ruleId,
        bool active,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var rule = await context.PutawayRules.SingleOrDefaultAsync(
            candidate => candidate.Id == ruleId,
            cancellationToken);
        if (rule is null)
        {
            return Result.Failure<PutawayRuleDto>(WmsErrors.NotFound(
                "putaway_rule.not_found",
                "The putaway rule was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.LocationsManage,
            rule.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PutawayRuleDto>();
        }

        try
        {
            rule.SetActive(active);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    active ? WmsAuditActions.PutawayRuleActivated : WmsAuditActions.PutawayRuleDeactivated,
                    WmsAuditEntityTypes.PutawayRule,
                    rule.Code,
                    rule.WarehouseId,
                    After: ToAuditValues(rule),
                    ActorUserId: userId),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(rule));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not change putaway rule {RuleId} active state", ruleId);
            return Result.Failure<PutawayRuleDto>(WmsErrors.FromException(
                exception,
                "putaway_rule.activation_failed",
                "The putaway rule active state could not be changed."));
        }
    }

    public async Task<Result<PutawaySuggestionResultDto>> SuggestAsync(
        PutawaySuggestionInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var permission = input.Simulation
            ? WmsPermissions.LocationsManage
            : WmsPermissions.PutawayExecute;
        var authorization = await warehouseAccessService.AuthorizeAsync(
            permission,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<PutawaySuggestionResultDto>();
        }

        if (input.Quantity <= 0)
        {
            return Result.Failure<PutawaySuggestionResultDto>(WmsErrors.Validation(
                "putaway_suggestion.quantity_invalid",
                "Suggestion quantity must be greater than zero."));
        }

        try
        {
            var item = await context.Items.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == input.ItemId, cancellationToken);
            if (item is null)
            {
                return Result.Failure<PutawaySuggestionResultDto>(WmsErrors.NotFound(
                    "item.not_found",
                    "The item was not found."));
            }

            if (!item.IsActive)
            {
                return Result.Failure<PutawaySuggestionResultDto>(WmsErrors.BusinessRule(
                    "item.inactive",
                    "Inactive items cannot receive a putaway suggestion."));
            }

            var lot = input.LotId.HasValue
                ? await context.Lots.AsNoTracking().SingleOrDefaultAsync(
                    candidate => candidate.Id == input.LotId.Value,
                    cancellationToken)
                : null;
            if (input.LotId.HasValue && lot is null)
            {
                return Result.Failure<PutawaySuggestionResultDto>(WmsErrors.NotFound(
                    "lot.not_found",
                    "The lot was not found."));
            }

            var atUtc = NormalizeUtc(input.AtUtc ?? DateTime.UtcNow);
            var rules = await context.PutawayRules.AsNoTracking()
                .Where(rule => rule.WarehouseId == input.WarehouseId &&
                               rule.IsActive &&
                               (input.Simulation || !rule.IsSimulation) &&
                               rule.EffectiveFromUtc <= atUtc &&
                               (!rule.EffectiveToUtc.HasValue || rule.EffectiveToUtc > atUtc))
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Id)
                .ToListAsync(cancellationToken);
            var locations = await context.Locations.AsNoTracking()
                .Where(location => location.WarehouseId == input.WarehouseId && location.IsActive)
                .OrderBy(location => location.Priority)
                .ThenBy(location => location.Code)
                .ToListAsync(cancellationToken);
            var stockLocationIds = locations.Select(location => location.Id).ToArray();
            var stocks = (await context.Stock.AsNoTracking()
                .Include(stock => stock.Item)
                .Where(stock => stockLocationIds.Contains(stock.LocationId))
                .ToListAsync(cancellationToken))
                .Where(stock => stock.QuantityAvailable.Value > 0 || stock.QuantityReserved.Value > 0)
                .ToArray();

            var matchingRules = rules
                .Where(rule => MatchesRule(rule, input, item, lot))
                .ToArray();
            var suggestions = new List<InternalSuggestion>();
            var rejections = new List<PutawayLocationRejectionDto>();
            foreach (var rule in matchingRules)
            {
                var ruleLocations = rule.FixedLocationId.HasValue
                    ? locations.Where(location => location.Id == rule.FixedLocationId.Value)
                    : locations;
                foreach (var location in ruleLocations)
                {
                    var evaluation = EvaluateLocation(rule, location, stocks, item, input);
                    if (!evaluation.IsValid)
                    {
                        AddRejection(rejections, new PutawayLocationRejectionDto(
                            location.Id,
                            location.Code,
                            rule.Id,
                            rule.Code,
                            evaluation.Code!,
                            evaluation.Reason!));
                        continue;
                    }

                    suggestions.Add(new InternalSuggestion(
                        location,
                        rule,
                        evaluation.Score,
                        evaluation.Explanation!));
                }
            }

            var selected = suggestions
                .GroupBy(candidate => candidate.Location.Id)
                .Select(group => group
                    .OrderByDescending(candidate => candidate.Rule.Priority)
                    .ThenByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Location.Priority)
                    .ThenBy(candidate => candidate.Location.Code)
                    .First())
                .OrderByDescending(candidate => candidate.Rule.Priority)
                .ThenByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Location.Priority)
                .ThenBy(candidate => candidate.Location.Code)
                .Take(25)
                .ToArray();

            if (selected.Length == 0)
            {
                var fallback = matchingRules
                    .Where(rule => rule.FallbackLocationId.HasValue)
                    .SelectMany(rule => locations
                        .Where(location => location.Id == rule.FallbackLocationId)
                        .Select(location => (Rule: rule, Location: location)))
                    .OrderByDescending(value => value.Rule.Priority)
                    .ThenBy(value => value.Location.Priority)
                    .FirstOrDefault();
                if (fallback.Location is not null)
                {
                    var fallbackEvaluation = EvaluateLocation(
                        fallback.Rule,
                        fallback.Location,
                        stocks,
                        item,
                        input,
                        ignoreStrategy: true);
                    if (fallbackEvaluation.IsValid)
                    {
                        selected =
                        [
                            new InternalSuggestion(
                                fallback.Location,
                                fallback.Rule,
                                fallbackEvaluation.Score,
                                $"Configured fallback location: {fallbackEvaluation.Explanation}")
                        ];
                    }
                    else
                    {
                        AddRejection(rejections, new PutawayLocationRejectionDto(
                            fallback.Location.Id,
                            fallback.Location.Code,
                            fallback.Rule.Id,
                            fallback.Rule.Code,
                            fallbackEvaluation.Code!,
                            fallbackEvaluation.Reason!));
                    }
                }
            }

            var mapped = selected
                .Select((candidate, index) => new PutawayLocationSuggestionDto(
                    candidate.Location.Id,
                    candidate.Location.Code,
                    candidate.Location.Name,
                    candidate.Location.Type,
                    candidate.Rule.Id,
                    candidate.Rule.Code,
                    candidate.Rule.Strategy,
                    candidate.Rule.Priority,
                    candidate.Score,
                    candidate.Explanation))
                .ToArray();
            return Result.Success(new PutawaySuggestionResultDto(
                mapped,
                rejections,
                input.Simulation,
                mapped.Length > 0,
                mapped.Length > 0
                    ? null
                    : "No active putaway rule produced a valid location; route the receipt to the configured exception or staging process."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Putaway suggestion evaluation failed for item {ItemId} in warehouse {WarehouseId}",
                input.ItemId,
                input.WarehouseId);
            return Result.Failure<PutawaySuggestionResultDto>(WmsErrors.FromException(
                exception,
                "putaway_suggestion.failed",
                "Putaway suggestions could not be evaluated."));
        }
    }

    private async Task<Result> ValidateReferencesAsync(
        PutawayRuleInput input,
        CancellationToken cancellationToken)
    {
        if (!await context.Warehouses.AnyAsync(
                warehouse => warehouse.Id == input.WarehouseId && warehouse.IsActive,
                cancellationToken))
        {
            return Result.Failure(WmsErrors.NotFound(
                "warehouse.not_found",
                "The selected warehouse was not found or is inactive."));
        }

        if (input.ItemId.HasValue && !await context.Items.AnyAsync(
                item => item.Id == input.ItemId.Value && item.IsActive,
                cancellationToken))
        {
            return Result.Failure(WmsErrors.NotFound(
                "item.not_found",
                "The selected item was not found or is inactive."));
        }

        if (input.SupplierId.HasValue && !await context.Suppliers.AnyAsync(
                supplier => supplier.Id == input.SupplierId.Value && supplier.IsActive,
                cancellationToken))
        {
            return Result.Failure(WmsErrors.NotFound(
                "supplier.not_found",
                "The selected supplier was not found or is inactive."));
        }

        var locationIds = new[] { input.FixedLocationId, input.FallbackLocationId }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (locationIds.Length > 0)
        {
            var validLocationIds = await context.Locations
                .Where(location => location.WarehouseId == input.WarehouseId &&
                                   locationIds.Contains(location.Id))
                .Select(location => location.Id)
                .ToListAsync(cancellationToken);
            if (validLocationIds.Count != locationIds.Length)
            {
                return Result.Failure(WmsErrors.Validation(
                    "putaway_rule.location_warehouse_mismatch",
                    "Fixed and fallback locations must belong to the selected warehouse."));
            }
        }

        return Result.Success();
    }

    private static bool MatchesRule(
        PutawayRule rule,
        PutawaySuggestionInput input,
        Item item,
        Lot? lot)
    {
        return (!rule.ItemId.HasValue || rule.ItemId == input.ItemId) &&
               (string.IsNullOrWhiteSpace(rule.ItemCategory) ||
                string.Equals(rule.ItemCategory, item.Category, StringComparison.OrdinalIgnoreCase)) &&
               (!rule.SupplierId.HasValue || rule.SupplierId == input.SupplierId) &&
               (string.IsNullOrWhiteSpace(rule.PackageType) ||
                string.Equals(rule.PackageType, input.PackageType, StringComparison.OrdinalIgnoreCase)) &&
               (!rule.LicensePlateType.HasValue || rule.LicensePlateType == input.LicensePlateType) &&
               (!rule.InventoryStatusId.HasValue || rule.InventoryStatusId == input.InventoryStatusId) &&
               (!rule.LotStatus.HasValue || rule.LotStatus == lot?.Status) &&
               (!rule.MinimumTemperatureCelsius.HasValue ||
                (item.MinimumTemperatureCelsius.HasValue &&
                 item.MinimumTemperatureCelsius >= rule.MinimumTemperatureCelsius)) &&
               (!rule.MaximumTemperatureCelsius.HasValue ||
                (item.MaximumTemperatureCelsius.HasValue &&
                 item.MaximumTemperatureCelsius <= rule.MaximumTemperatureCelsius)) &&
               (string.IsNullOrWhiteSpace(rule.StorageProfile) ||
                string.Equals(rule.StorageProfile, item.StorageProfile, StringComparison.OrdinalIgnoreCase)) &&
               (string.IsNullOrWhiteSpace(rule.SourceProcess) ||
                string.Equals(rule.SourceProcess, input.SourceProcess, StringComparison.OrdinalIgnoreCase));
    }

    private static LocationEvaluation EvaluateLocation(
        PutawayRule rule,
        Location location,
        IReadOnlyList<Stock> stocks,
        Item item,
        PutawaySuggestionInput input,
        bool ignoreStrategy = false)
    {
        if (!IsPutawayLocationType(location.Type))
        {
            return LocationEvaluation.Invalid(
                "location.type_not_valid",
                $"Location '{location.Code}' is not a valid putaway destination.");
        }

        if (rule.TargetLocationType.HasValue && rule.TargetLocationType != location.Type)
        {
            return LocationEvaluation.Invalid(
                "location.type_mismatch",
                $"Location '{location.Code}' does not match the rule target location type.");
        }

        if (!string.IsNullOrWhiteSpace(rule.TargetStorageProfile) &&
            !string.Equals(rule.TargetStorageProfile, location.StorageProfile, StringComparison.OrdinalIgnoreCase))
        {
            return LocationEvaluation.Invalid(
                "location.storage_profile_mismatch",
                $"Location '{location.Code}' does not match the rule storage profile.");
        }

        if (!string.IsNullOrWhiteSpace(item.StorageProfile) &&
            !string.Equals(item.StorageProfile, location.StorageProfile, StringComparison.OrdinalIgnoreCase))
        {
            return LocationEvaluation.Invalid(
                "location.item_storage_profile_mismatch",
                $"Location '{location.Code}' does not match item storage profile '{item.StorageProfile}'.");
        }

        if (item.TemperatureControlled &&
            (!location.MinimumTemperatureCelsius.HasValue || !location.MaximumTemperatureCelsius.HasValue))
        {
            return LocationEvaluation.Invalid(
                "location.temperature_profile_missing",
                $"Location '{location.Code}' has no complete temperature profile for this item.");
        }

        if (item.MinimumTemperatureCelsius.HasValue &&
            location.MinimumTemperatureCelsius.HasValue &&
            location.MinimumTemperatureCelsius > item.MinimumTemperatureCelsius)
        {
            return LocationEvaluation.Invalid(
                "location.temperature_minimum_mismatch",
                $"Location '{location.Code}' cannot reach the item's minimum temperature.");
        }

        if (item.MaximumTemperatureCelsius.HasValue &&
            location.MaximumTemperatureCelsius.HasValue &&
            location.MaximumTemperatureCelsius < item.MaximumTemperatureCelsius)
        {
            return LocationEvaluation.Invalid(
                "location.temperature_maximum_mismatch",
                $"Location '{location.Code}' cannot maintain the item's maximum temperature.");
        }

        if (item.IsHazardous && string.IsNullOrWhiteSpace(location.HazardClass))
        {
            return LocationEvaluation.Invalid(
                "location.hazard_profile_missing",
                $"Location '{location.Code}' has no hazard profile for a hazardous item.");
        }

        if (!string.IsNullOrWhiteSpace(rule.HazardClass) &&
            !string.Equals(rule.HazardClass, location.HazardClass, StringComparison.OrdinalIgnoreCase))
        {
            return LocationEvaluation.Invalid(
                "location.hazard_class_mismatch",
                $"Location '{location.Code}' does not match the rule hazard class.");
        }

        var occupied = stocks.Where(stock => stock.LocationId == location.Id).ToArray();
        if (!location.AllowMixedItems && occupied.Any(stock => stock.ItemId != input.ItemId))
        {
            return LocationEvaluation.Invalid(
                "location.mixed_items_blocked",
                $"Location '{location.Code}' does not allow mixed items.");
        }

        if (!location.AllowMixedLots && occupied.Any(stock =>
                stock.ItemId == input.ItemId && stock.LotId != input.LotId))
        {
            return LocationEvaluation.Invalid(
                "location.mixed_lots_blocked",
                $"Location '{location.Code}' does not allow mixed lots.");
        }

        var current = new LocationCapacitySnapshot(
            occupied.Sum(stock => stock.QuantityAvailable.Value),
            occupied.Sum(stock => stock.QuantityAvailable.Value * (stock.Item?.NetWeightKg ?? 0m)),
            occupied.Sum(stock => stock.QuantityAvailable.Value * (stock.Item?.VolumeCubicMeters ?? 0m)),
            Lpns: occupied
                .Where(stock => stock.LicensePlateId.HasValue)
                .Select(stock => stock.LicensePlateId!.Value)
                .Distinct()
                .Count());
        var incoming = new LocationCapacitySnapshot(
            input.Quantity,
            input.Quantity * (item.NetWeightKg ?? 0m),
            input.Quantity * (item.VolumeCubicMeters ?? 0m),
            Pallets: input.LicensePlateType == LicensePlateType.Pallet ? 1 : 0,
            Lpns: input.LicensePlateId.HasValue &&
                  occupied.All(stock => stock.LicensePlateId != input.LicensePlateId)
                ? 1
                : 0);
        var capacityViolation = location.ValidateCapacity(current, incoming);
        if (capacityViolation is not null)
        {
            return LocationEvaluation.Invalid(capacityViolation.Code, capacityViolation.Message);
        }

        if (!ignoreStrategy &&
            rule.Strategy == PutawayRuleStrategy.EmptyLocation &&
            current.Units != 0)
        {
            return LocationEvaluation.Invalid(
                "location.not_empty",
                $"Location '{location.Code}' is not empty.");
        }

        if (!ignoreStrategy &&
            rule.Strategy == PutawayRuleStrategy.BulkStorage &&
            location.Type != LocationType.Bulk)
        {
            return LocationEvaluation.Invalid(
                "location.not_bulk",
                $"Location '{location.Code}' is not a bulk location.");
        }

        var matchingStock = occupied.Any(stock =>
            stock.ItemId == input.ItemId && stock.LotId == input.LotId);
        var score = rule.Strategy switch
        {
            PutawayRuleStrategy.FixedLocation => 10_000m,
            PutawayRuleStrategy.SameItemLot => matchingStock ? 9_000m : 1_000m,
            PutawayRuleStrategy.EmptyLocation => 8_000m,
            PutawayRuleStrategy.PickFaceFirst => location.Type == LocationType.PickFace
                ? 8_000m
                : 1_000m,
            PutawayRuleStrategy.BulkStorage => 8_000m,
            PutawayRuleStrategy.NearestSequence => 8_000m - location.Priority,
            PutawayRuleStrategy.CapacityAware => 8_000m + RemainingCapacityScore(location, current),
            _ => 0m
        };
        if (ignoreStrategy)
        {
            score = 1_000m + RemainingCapacityScore(location, current);
        }

        var explanation = rule.Strategy switch
        {
            PutawayRuleStrategy.FixedLocation => "Fixed location matched.",
            PutawayRuleStrategy.SameItemLot when matchingStock => "Consolidates with the same item and lot.",
            PutawayRuleStrategy.SameItemLot => "No same item/lot stock exists; ranked as a fallback.",
            PutawayRuleStrategy.EmptyLocation => "Location is empty and capacity-valid.",
            PutawayRuleStrategy.PickFaceFirst when location.Type == LocationType.PickFace => "Pick-face location ranked first.",
            PutawayRuleStrategy.BulkStorage => "Bulk-storage location matched.",
            PutawayRuleStrategy.NearestSequence => "Lowest location sequence priority among valid locations.",
            PutawayRuleStrategy.CapacityAware => "Capacity, dimensions, and location constraints are valid.",
            _ => "Location passed the configured rule."
        };
        return LocationEvaluation.Valid(score, explanation);
    }

    private static decimal RemainingCapacityScore(Location location, LocationCapacitySnapshot current)
    {
        if (!location.MaxUnits.HasValue || location.MaxUnits.Value <= 0)
        {
            return 0m;
        }

        return Math.Clamp(
            (location.MaxUnits.Value - current.Units) / location.MaxUnits.Value * 100m,
            0m,
            100m);
    }

    private static bool IsPutawayLocationType(LocationType type) => type is
        LocationType.Storage or
        LocationType.Zone or
        LocationType.Aisle or
        LocationType.Rack or
        LocationType.Bin or
        LocationType.PickFace or
        LocationType.Bulk or
        LocationType.Quarantine or
        LocationType.Damaged or
        LocationType.Returns;

    private static void AddRejection(
        List<PutawayLocationRejectionDto> rejections,
        PutawayLocationRejectionDto rejection)
    {
        if (rejections.Count < MaximumRejections)
        {
            rejections.Add(rejection);
        }
    }

    private static PutawayRuleDto Map(PutawayRule rule) => new(
        rule.Id,
        rule.WarehouseId,
        rule.Code,
        rule.Name,
        rule.Strategy,
        rule.Priority,
        rule.EffectiveFromUtc,
        rule.EffectiveToUtc,
        rule.ItemId,
        rule.ItemCategory,
        rule.SupplierId,
        rule.PackageType,
        rule.LicensePlateType,
        rule.InventoryStatusId,
        rule.LotStatus,
        rule.MinimumTemperatureCelsius,
        rule.MaximumTemperatureCelsius,
        rule.HazardClass,
        rule.StorageProfile,
        rule.SourceProcess,
        rule.FixedLocationId,
        rule.TargetLocationType,
        rule.TargetStorageProfile,
        rule.FallbackLocationId,
        rule.IsSimulation,
        rule.IsActive,
        rule.Notes,
        rule.Revision);

    private static Dictionary<string, object?> ToAuditValues(PutawayRule rule) =>
        new Dictionary<string, object?>
        {
            ["code"] = rule.Code,
            ["name"] = rule.Name,
            ["strategy"] = rule.Strategy.ToString(),
            ["priority"] = rule.Priority,
            ["effectiveFromUtc"] = rule.EffectiveFromUtc,
            ["effectiveToUtc"] = rule.EffectiveToUtc,
            ["itemId"] = rule.ItemId,
            ["itemCategory"] = rule.ItemCategory,
            ["supplierId"] = rule.SupplierId,
            ["fixedLocationId"] = rule.FixedLocationId,
            ["fallbackLocationId"] = rule.FallbackLocationId,
            ["isSimulation"] = rule.IsSimulation,
            ["isActive"] = rule.IsActive
        };

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private sealed record InternalSuggestion(
        Location Location,
        PutawayRule Rule,
        decimal Score,
        string Explanation);

    private sealed record LocationEvaluation(
        bool IsValid,
        decimal Score,
        string? Code,
        string? Reason,
        string? Explanation)
    {
        public static LocationEvaluation Valid(decimal score, string explanation) =>
            new(true, score, null, null, explanation);

        public static LocationEvaluation Invalid(string code, string reason) =>
            new(false, 0m, code, reason, null);
    }
}
