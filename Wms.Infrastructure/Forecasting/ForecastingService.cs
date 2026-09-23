using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Forecasting;

public sealed class ForecastingService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<ForecastingService> logger,
    ICurrentUser? currentUser = null) : IForecastingService
{
    public const string CurrentModelVersion = "baseline.v2";
    public const int MaximumPageSize = 100;
    public const int MaximumBatchPairs = 500;
    public const int MaximumSourceRowsPerKind = 25_000;
    public const int MaximumLedgerRows = 50_000;
    private const int TrainingWindowPeriods = 52;
    private const int MovingAverageWindow = 4;

    public async Task<Result<ForecastRecalculationResultDto>> RecalculateAsync(
        ForecastingRecalculationQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.WarehouseId is <= 0 || query.ItemId is <= 0 ||
            (query.ItemId.HasValue && !query.WarehouseId.HasValue))
        {
            return Result.Failure<ForecastRecalculationResultDto>(WmsErrors.Validation(
                "forecast.scope_invalid",
                "A warehouse is required for an item-specific recalculation."));
        }

        if (!Enum.IsDefined(query.Granularity) ||
            query.HorizonPeriods is < 1 or > ForecastBaselineEngine.MaximumHorizonPeriods)
        {
            return Result.Failure<ForecastRecalculationResultDto>(WmsErrors.Validation(
                "forecast.options_invalid",
                "The requested period and horizon are outside the supported bounds."));
        }

        if (query.WarehouseId.HasValue && !string.IsNullOrWhiteSpace(currentUser?.UserId))
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.ForecastingRecalculate,
                query.WarehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization.ToFailure<ForecastRecalculationResultDto>();
            }
        }

        var sourceCutoffUtc = DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
        var pairs = await ResolvePairsAsync(query, sourceCutoffUtc, cancellationToken);
        if (pairs.IsFailure)
        {
            return pairs.ToFailure<ForecastRecalculationResultDto>();
        }

        var outcomes = new List<PairRecalculationOutcome>(pairs.Value.Pairs.Count);
        foreach (var pair in pairs.Value.Pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await RecalculatePairAsync(
                pair,
                query.Granularity,
                query.HorizonPeriods,
                sourceCutoffUtc,
                cancellationToken));
        }

        var flags = outcomes.SelectMany(outcome => outcome.DataQualityFlags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(flag => flag, StringComparer.Ordinal)
            .ToList();
        if (pairs.Value.HasMore)
        {
            flags.Add("scope-batch-capped-at-500-item-warehouse-pairs");
        }

        return Result.Success(new ForecastRecalculationResultDto(
            outcomes.Count,
            outcomes.Count(outcome => outcome.Created),
            outcomes.Count(outcome => !outcome.Created),
            flags.Distinct(StringComparer.Ordinal).OrderBy(flag => flag, StringComparer.Ordinal).ToArray()));
    }

    public async Task<Result<ForecastRunPageDto>> SearchAsync(
        ForecastingSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.WarehouseId is <= 0 || query.ItemId is <= 0 ||
            query.Page < 1 || query.PageSize is < 1 or > MaximumPageSize)
        {
            return Result.Failure<ForecastRunPageDto>(WmsErrors.Validation(
                "forecast.page_invalid",
                $"Page must be positive and page size must be between 1 and {MaximumPageSize}."));
        }

        var authorization = await AuthorizeReadAsync(query.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ForecastRunPageDto>();
        }

        var runs = context.ForecastRuns.AsNoTracking().AsQueryable();
        if (query.WarehouseId.HasValue)
        {
            runs = runs.Where(run => run.WarehouseId == query.WarehouseId.Value);
        }
        else
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            if (!scope.HasGlobalAccess)
            {
                var warehouseIds = scope.WarehouseIds.ToArray();
                if (warehouseIds.Length == 0)
                {
                    return Result.Success(new ForecastRunPageDto(query.Page, query.PageSize, 0, []));
                }

                runs = runs.Where(run => warehouseIds.Contains(run.WarehouseId));
            }
        }

        if (query.ItemId.HasValue)
        {
            runs = runs.Where(run => run.ItemId == query.ItemId.Value);
        }

        var totalItems = await runs.CountAsync(cancellationToken);
        var rows = await runs
            .OrderByDescending(run => run.CreatedAtUtc)
            .ThenByDescending(run => run.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(run => new
            {
                run.Id,
                run.WarehouseId,
                run.ItemId,
                ItemSku = run.Item.Sku,
                ItemName = run.Item.Name,
                run.Granularity,
                run.DataStatus,
                run.SelectedModel,
                run.ModelVersion,
                run.SourceCutoffUtc,
                WarehouseTimeZone = run.Warehouse.TimeZone,
                run.InputPeriodStart,
                run.InputPeriodEnd,
                run.HorizonPeriods,
                run.RiskLevel,
                run.DataQualityFlagsJson,
                ForecastPeriodCount = run.Points.Count(point => point.ForecastQuantity.HasValue)
            })
            .ToListAsync(cancellationToken);

        return Result.Success(new ForecastRunPageDto(
            query.Page,
            query.PageSize,
            totalItems,
            rows.Select(row => new ForecastRunSummaryDto(
                row.Id,
                row.WarehouseId,
                row.ItemId,
                row.ItemSku,
                row.ItemName,
                ParseEnum<ForecastGranularity>(row.Granularity),
                ParseEnum<ForecastDataStatus>(row.DataStatus),
                ParseNullableEnum<ForecastModelKind>(row.SelectedModel),
                row.ModelVersion,
                row.SourceCutoffUtc,
                ResolveStaleness(
                    row.InputPeriodEnd,
                    row.WarehouseTimeZone,
                    ParseEnum<ForecastGranularity>(row.Granularity)),
                row.InputPeriodStart,
                row.InputPeriodEnd,
                row.HorizonPeriods,
                ParseEnum<ForecastRiskLevel>(row.RiskLevel),
                row.ForecastPeriodCount,
                DeserializeStringList(row.DataQualityFlagsJson)))
                .ToArray()));
    }

    public async Task<Result<ForecastRunDto>> GetAsync(
        int runId,
        CancellationToken cancellationToken = default)
    {
        if (runId <= 0)
        {
            return Result.Failure<ForecastRunDto>(WmsErrors.Validation(
                "forecast.run_id_invalid",
                "A positive forecast run identifier is required."));
        }

        var run = await context.ForecastRuns
            .AsNoTracking()
            .Include(entity => entity.Item)
            .Include(entity => entity.Warehouse)
            .Include(entity => entity.Points)
            .Include(entity => entity.Overrides)
            .SingleOrDefaultAsync(entity => entity.Id == runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<ForecastRunDto>(WmsErrors.NotFound(
                "forecast.run_not_found",
                "The requested forecast run was not found."));
        }

        var authorization = await AuthorizeReadAsync(run.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ForecastRunDto>();
        }

        var latestOverrides = run.Overrides
            .GroupBy(overrideEntity => overrideEntity.PeriodStart)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(overrideEntity => overrideEntity.Version).First());
        var dto = new ForecastRunDto(
            run.Id,
            run.WarehouseId,
            run.ItemId,
            run.Item.Sku,
            run.Item.Name,
            run.BaseUnitOfMeasure,
            ParseEnum<ForecastGranularity>(run.Granularity),
            ParseEnum<ForecastDataStatus>(run.DataStatus),
            ParseNullableEnum<ForecastModelKind>(run.SelectedModel),
            run.ModelVersion,
            run.InputFingerprint,
            run.SourceCutoffUtc,
            run.InputPeriodStart,
            run.InputPeriodEnd,
            run.HorizonPeriods,
            run.TrainingWindowPeriods,
            run.MovingAverageWindow,
            DeserializeScores(run.BacktestScoresJson),
            run.ProjectedStockoutPeriod,
            run.DaysOfSupply,
            ParseEnum<ForecastRiskLevel>(run.RiskLevel),
            run.InsufficientDataReason,
            ResolveStaleness(
                run.InputPeriodEnd,
                run.Warehouse.TimeZone,
                ParseEnum<ForecastGranularity>(run.Granularity)),
            DeserializeStringList(run.DataQualityFlagsJson),
            run.Points.OrderBy(point => point.PeriodStart)
                .Select(point =>
                {
                    latestOverrides.TryGetValue(point.PeriodStart, out var overrideEntity);
                    return new ForecastRunPointDto(
                        point.PeriodStart,
                        point.ActualDemand,
                        point.ForecastQuantity,
                        point.LowerBound,
                        point.UpperBound,
                        point.WasStockoutCensored,
                        overrideEntity?.Quantity,
                        overrideEntity?.Version);
                })
                .ToArray(),
            run.CreatedAtUtc);
        return Result.Success(dto);
    }

    public async Task<Result<ForecastActualComparisonDto>> CompareActualsAsync(
        int runId,
        CancellationToken cancellationToken = default)
    {
        var run = await context.ForecastRuns.AsNoTracking()
            .Include(entity => entity.Item)
            .Include(entity => entity.Warehouse)
            .Include(entity => entity.Points)
            .SingleOrDefaultAsync(entity => entity.Id == runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<ForecastActualComparisonDto>(WmsErrors.NotFound(
                "forecast.run_not_found",
                "The requested forecast run was not found."));
        }

        var authorization = await AuthorizeReadAsync(run.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ForecastActualComparisonDto>();
        }

        var forecasts = run.Points.Where(point => point.ForecastQuantity.HasValue).ToArray();
        var cutoff = DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
        if (forecasts.Length == 0)
        {
            return Result.Success(new ForecastActualComparisonDto(
                run.Id, 0, null, null, null, cutoff, run.BaseUnitOfMeasure));
        }

        var source = await LoadSourceEventsAsync(
            run.WarehouseId, run.ItemId, run.BaseUnitOfMeasure, cutoff, cancellationToken);
        var conversions = await LoadUnitConversionsAsync(run.ItemId, cancellationToken);
        var series = ForecastDemandSeriesBuilder.Build(
            source.Events,
            conversions,
            run.BaseUnitOfMeasure,
            run.Warehouse.TimeZone,
            ParseEnum<ForecastGranularity>(run.Granularity),
            cutoff);
        var actuals = series.History.ToDictionary(point => point.PeriodStart, point => point.DemandQuantity);
        var comparisons = source.ExceededLimit || !series.IsComplete
            ? []
            : forecasts
                .Where(point => actuals.ContainsKey(point.PeriodStart))
                .Select(point => (Forecast: point.ForecastQuantity!.Value, Actual: actuals[point.PeriodStart]))
                .ToArray();
        var errors = comparisons.Select(pair => Math.Abs(pair.Forecast - pair.Actual)).ToArray();
        var percentageErrors = comparisons
            .Where(pair => pair.Actual > 0m)
            .Select(pair => Math.Abs(pair.Forecast - pair.Actual) / pair.Actual * 100m)
            .ToArray();
        return Result.Success(new ForecastActualComparisonDto(
            run.Id,
            comparisons.Length,
            errors.Length == 0 ? null : decimal.Round(errors.Average(), 4),
            percentageErrors.Length == 0 ? null : decimal.Round(percentageErrors.Average(), 4),
            comparisons.Length == 0 ? null : decimal.Round(
                comparisons.Average(pair => pair.Forecast - pair.Actual), 4),
            cutoff,
            run.BaseUnitOfMeasure));
    }

    public async Task<Result<ForecastOverrideDto>> CreateOverrideAsync(
        int runId,
        ForecastOverrideInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (runId <= 0 || input.Quantity < 0m || input.Quantity > 1_000_000_000_000m ||
            string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1_000 ||
            string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<ForecastOverrideDto>(WmsErrors.Validation(
                "forecast.override_invalid",
                "A non-negative quantity, bounded reason, run, period, and authenticated actor are required."));
        }

        var run = await context.ForecastRuns
            .Include(entity => entity.Points)
            .SingleOrDefaultAsync(entity => entity.Id == runId, cancellationToken);
        if (run is null)
        {
            return Result.Failure<ForecastOverrideDto>(WmsErrors.NotFound(
                "forecast.run_not_found",
                "The requested forecast run was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ForecastingOverride,
            run.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<ForecastOverrideDto>();
        }

        if (!run.Points.Any(point =>
                point.PeriodStart == input.PeriodStart && point.ForecastQuantity.HasValue))
        {
            return Result.Failure<ForecastOverrideDto>(WmsErrors.Validation(
                "forecast.override_period_invalid",
                "An override can only target a forecast period in this run."));
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var version = (await context.ForecastOverrides
            .Where(entity => entity.ForecastRunId == runId && entity.PeriodStart == input.PeriodStart)
            .Select(entity => (int?)entity.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var createdAtUtc = DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
        var entity = new ForecastOverrideEntity
        {
            ForecastRunId = runId,
            PeriodStart = input.PeriodStart,
            Version = version,
            Quantity = input.Quantity,
            Reason = input.Reason.Trim(),
            CreatedByUserId = actorUserId.Trim(),
            CreatedAtUtc = createdAtUtc
        };
        context.ForecastOverrides.Add(entity);
        await auditWriter.RecordAsync(new AuditRecord(
            WmsAuditActions.ForecastOverrideCreated,
            WmsAuditEntityTypes.ForecastOverride,
            $"{runId}:{input.PeriodStart:yyyy-MM-dd}:{version}",
            run.WarehouseId,
            After: new Dictionary<string, object?>
            {
                ["forecastRunId"] = runId,
                ["itemId"] = run.ItemId,
                ["periodStart"] = input.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["quantity"] = input.Quantity,
                ["version"] = version,
                ["reason"] = input.Reason.Trim()
            },
            ActorUserId: actorUserId.Trim()), cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            logger.LogWarning(
                "A forecast override for run {ForecastRunId} period {PeriodStart} could not be saved",
                runId,
                input.PeriodStart);
            return Result.Failure<ForecastOverrideDto>(WmsErrors.FromException(
                exception,
                "forecast.override_save_failed",
                "The forecast override could not be saved."));
        }

        return Result.Success(new ForecastOverrideDto(
            runId,
            input.PeriodStart,
            entity.Quantity,
            entity.Version,
            entity.Reason,
            entity.CreatedByUserId,
            entity.CreatedAtUtc));
    }

    private async Task<Result<PairBatch>> ResolvePairsAsync(
        ForecastingRecalculationQuery query,
        DateTime sourceCutoffUtc,
        CancellationToken cancellationToken)
    {
        if (query.ItemId.HasValue)
        {
            var itemExists = await context.Items.AsNoTracking()
                .AnyAsync(item => item.Id == query.ItemId.Value, cancellationToken);
            var warehouseExists = await context.Warehouses.AsNoTracking()
                .AnyAsync(warehouse => warehouse.Id == query.WarehouseId && warehouse.IsActive, cancellationToken);
            return itemExists && warehouseExists
                ? Result.Success(new PairBatch(
                    [new WarehouseItemPair(query.WarehouseId!.Value, query.ItemId.Value)],
                    false))
                : Result.Failure<PairBatch>(WmsErrors.NotFound(
                    "forecast.scope_not_found",
                    "The requested item or active warehouse was not found."));
        }

        var sourceLookbackUtc = sourceCutoffUtc.AddYears(-10).AddDays(-5);
        var pairQuery = context.ShipmentLines.AsNoTracking()
            .Where(line => line.Shipment.ActualShipAtUtc.HasValue &&
                line.Shipment.ActualShipAtUtc.Value >= sourceLookbackUtc &&
                line.Shipment.ActualShipAtUtc.Value <= sourceCutoffUtc &&
                line.Shipment.Status != Wms.Domain.Enums.ShipmentStatus.Cancelled &&
                line.Shipment.Warehouse.IsActive &&
                (!query.WarehouseId.HasValue || line.Shipment.WarehouseId == query.WarehouseId.Value))
            .Select(line => new
            {
                WarehouseId = line.Shipment.WarehouseId,
                line.ItemId
            })
            .Distinct()
            .OrderBy(pair => pair.WarehouseId)
            .ThenBy(pair => pair.ItemId);
        var total = await pairQuery.CountAsync(cancellationToken);
        if (total == 0)
        {
            return Result.Success(new PairBatch([], false));
        }

        var dayOffset = Math.Max(0, clock.UtcNow.UtcDateTime.DayOfYear - 1);
        var offset = (dayOffset * MaximumBatchPairs) % total;
        var rows = await pairQuery.Skip(offset).Take(MaximumBatchPairs).ToArrayAsync(cancellationToken);
        if (rows.Length < MaximumBatchPairs && offset > 0)
        {
            var wrapRows = await pairQuery.Take(MaximumBatchPairs - rows.Length).ToArrayAsync(cancellationToken);
            rows = rows.Concat(wrapRows).ToArray();
        }

        return Result.Success(new PairBatch(
            rows.Select(row => new WarehouseItemPair(row.WarehouseId, row.ItemId)).ToArray(),
            total > MaximumBatchPairs));
    }

    private async Task<PairRecalculationOutcome> RecalculatePairAsync(
        WarehouseItemPair pair,
        ForecastGranularity granularity,
        int horizonPeriods,
        DateTime sourceCutoffUtc,
        CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses.AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == pair.WarehouseId && entity.IsActive, cancellationToken);
        var item = await context.Items.AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == pair.ItemId, cancellationToken);
        if (warehouse is null || item is null)
        {
            throw new InvalidOperationException("A forecast source pair lost its item or warehouse during recalculation.");
        }

        var source = await LoadSourceEventsAsync(
            pair.WarehouseId, pair.ItemId, item.UnitOfMeasure, sourceCutoffUtc, cancellationToken);
        var conversions = await LoadUnitConversionsAsync(pair.ItemId, cancellationToken);
        var initialSeries = ForecastDemandSeriesBuilder.Build(
            source.Events, conversions, item.UnitOfMeasure, warehouse.TimeZone,
            granularity, sourceCutoffUtc);
        var qualityFlags = new SortedSet<string>(initialSeries.DataQualityFlags, StringComparer.Ordinal);
        if (source.ExceededLimit)
        {
            qualityFlags.Add("source-event-cap-exceeded");
        }

        var available = await LoadAvailableSupplyAsync(
            pair.WarehouseId, pair.ItemId, item.UnitOfMeasure, conversions, cancellationToken);
        qualityFlags.UnionWith(available.DataQualityFlags);
        var riskPolicy = await LoadRiskPolicyAsync(
            pair.WarehouseId,
            pair.ItemId,
            granularity,
            sourceCutoffUtc,
            cancellationToken);
        if (!riskPolicy.IsKnown)
        {
            qualityFlags.Add("effective-lead-time-policy-missing");
        }

        var stockoutEvidence = await LoadStockoutEvidenceAsync(
            pair.WarehouseId, pair.ItemId, item.UnitOfMeasure, conversions,
            warehouse.TimeZone, granularity, initialSeries.History, sourceCutoffUtc, cancellationToken);
        if (stockoutEvidence.DataQualityFlag is not null)
        {
            qualityFlags.Add(stockoutEvidence.DataQualityFlag);
        }

        var series = ForecastDemandSeriesBuilder.Build(
            source.Events, conversions, item.UnitOfMeasure, warehouse.TimeZone,
            granularity, sourceCutoffUtc, stockoutEvidence.PeriodStarts);
        qualityFlags.UnionWith(series.DataQualityFlags);
        qualityFlags.Add("available-supply-uses-current-unreserved-stock-only");

        var historyIsComplete = series.IsComplete && !source.ExceededLimit;
        var history = historyIsComplete ? series.History : [];
        var request = new ForecastRequest(
            pair.ItemId,
            pair.WarehouseId,
            granularity,
            horizonPeriods,
            history,
            TrainingWindowPeriods,
            MovingAverageWindow,
            available.IsComplete ? Math.Max(0m, available.Quantity) : 0m,
            LeadTimePeriods: riskPolicy.LeadTimePeriods,
            SafetyStockQuantity: riskPolicy.SafetyStockQuantity,
            ModelVersion: CurrentModelVersion);
        var generated = ForecastBaselineEngine.Generate(request);
        if (generated.IsFailure)
        {
            throw new InvalidOperationException("A validated forecast request was rejected by the baseline engine.");
        }

        var result = generated.Value;
        var fingerprint = CombineFingerprints(
            result.InputFingerprint,
            series.SourceFingerprint,
            source.SourceFingerprint,
            string.Join("|", qualityFlags));
        result = result with { InputFingerprint = fingerprint };
        if (!available.IsComplete || stockoutEvidence.PeriodStarts is null || !riskPolicy.IsKnown)
        {
            result = result with
            {
                RiskLevel = ForecastRiskLevel.Unknown,
                ProjectedStockoutPeriod = null,
                DaysOfSupply = null
            };
        }

        if (!historyIsComplete)
        {
            result = result with
            {
                DataStatus = ForecastDataStatus.InsufficientData,
                SelectedModel = null,
                BacktestScores = [],
                Periods = [],
                RiskLevel = ForecastRiskLevel.Unknown,
                ProjectedStockoutPeriod = null,
                DaysOfSupply = null,
                InsufficientDataReason = source.ExceededLimit
                    ? "The source event cap was reached; a partial source series is not forecast."
                    : "The source history could not be normalized completely to the item's base unit."
            };
        }

        var existing = await context.ForecastRuns.AsNoTracking()
            .AnyAsync(run => run.WarehouseId == pair.WarehouseId &&
                run.ItemId == pair.ItemId &&
                run.Granularity == granularity.ToString() &&
                run.HorizonPeriods == horizonPeriods &&
                run.InputFingerprint == result.InputFingerprint,
                cancellationToken);
        if (existing)
        {
            return new PairRecalculationOutcome(false, qualityFlags.ToArray());
        }

        var createdAtUtc = DateTime.SpecifyKind(clock.UtcNow.UtcDateTime, DateTimeKind.Utc);
        var bestScore = result.BacktestScores
            .OrderBy(score => score.Metrics.MeanAbsoluteError)
            .ThenBy(score => score.Model)
            .FirstOrDefault();
        var runEntity = new ForecastRunEntity
        {
            WarehouseId = pair.WarehouseId,
            ItemId = pair.ItemId,
            Granularity = result.Granularity.ToString(),
            DataStatus = result.DataStatus.ToString(),
            SelectedModel = result.SelectedModel?.ToString(),
            ModelVersion = result.ModelVersion,
            InputFingerprint = result.InputFingerprint,
            SourceCutoffUtc = sourceCutoffUtc,
            InputPeriodStart = result.InputPeriodStart,
            InputPeriodEnd = result.InputPeriodEnd,
            HorizonPeriods = result.HorizonPeriods,
            TrainingWindowPeriods = TrainingWindowPeriods,
            MovingAverageWindow = MovingAverageWindow,
            OnHandQuantity = available.IsComplete ? Math.Max(0m, available.Quantity) : 0m,
            OnOrderQuantity = 0m,
            InTransitQuantity = 0m,
            LeadTimePeriods = request.LeadTimePeriods,
            SafetyStockQuantity = request.SafetyStockQuantity,
            MeanAbsoluteError = bestScore?.Metrics.MeanAbsoluteError,
            MeanAbsolutePercentageError = bestScore?.Metrics.MeanAbsolutePercentageError,
            Bias = bestScore?.Metrics.Bias,
            BacktestEvaluatedPeriods = result.BacktestScores
                .Select(score => score.Metrics.EvaluatedPeriods).DefaultIfEmpty().Max(),
            BacktestScoresJson = JsonSerializer.Serialize(result.BacktestScores),
            DataQualityFlagsJson = JsonSerializer.Serialize(qualityFlags.ToArray()),
            BaseUnitOfMeasure = item.UnitOfMeasure,
            InsufficientDataReason = result.InsufficientDataReason,
            ProjectedStockoutPeriod = result.ProjectedStockoutPeriod,
            DaysOfSupply = result.DaysOfSupply,
            RiskLevel = result.RiskLevel.ToString(),
            CreatedAtUtc = createdAtUtc
        };
        foreach (var point in series.History)
        {
            runEntity.Points.Add(new ForecastRunPointEntity
            {
                PeriodStart = point.PeriodStart,
                ActualDemand = point.DemandQuantity,
                WasStockoutCensored = point.WasStockout
            });
        }

        foreach (var period in result.Periods)
        {
            runEntity.Points.Add(new ForecastRunPointEntity
            {
                PeriodStart = period.PeriodStart,
                ForecastQuantity = period.ForecastQuantity,
                LowerBound = period.LowerBound,
                UpperBound = period.UpperBound
            });
        }

        context.ForecastRuns.Add(runEntity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new PairRecalculationOutcome(true, qualityFlags.ToArray());
        }
        catch (DbUpdateException exception)
        {
            foreach (var entry in context.ChangeTracker.Entries()
                         .Where(entry => entry.State == EntityState.Added &&
                             entry.Entity is ForecastRunEntity or ForecastRunPointEntity))
            {
                entry.State = EntityState.Detached;
            }

            var duplicate = await context.ForecastRuns.AsNoTracking()
                .AnyAsync(run => run.WarehouseId == pair.WarehouseId &&
                    run.ItemId == pair.ItemId &&
                    run.Granularity == granularity.ToString() &&
                    run.HorizonPeriods == horizonPeriods &&
                    run.InputFingerprint == result.InputFingerprint,
                    cancellationToken);
            if (duplicate)
            {
                return new PairRecalculationOutcome(false, qualityFlags.ToArray());
            }

            logger.LogError(exception, "Unable to persist forecast run for warehouse {WarehouseId} item {ItemId}",
                pair.WarehouseId, pair.ItemId);
            throw new InvalidOperationException("A forecast run could not be persisted.");
        }
    }

    private async Task<SourceEventsResult> LoadSourceEventsAsync(
        int warehouseId,
        int itemId,
        string targetUnit,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var lookbackUtc = cutoffUtc.AddYears(-10).AddDays(-5);
        var shipments = await context.ShipmentLines.AsNoTracking()
            .Where(line => line.ItemId == itemId &&
                line.Shipment.WarehouseId == warehouseId &&
                line.Shipment.ActualShipAtUtc.HasValue &&
                line.Shipment.ActualShipAtUtc.Value >= lookbackUtc &&
                line.Shipment.ActualShipAtUtc.Value <= cutoffUtc &&
                line.Shipment.Status != Wms.Domain.Enums.ShipmentStatus.Cancelled)
            .OrderByDescending(line => line.Shipment.ActualShipAtUtc)
            .ThenByDescending(line => line.Id)
            .Take(MaximumSourceRowsPerKind + 1)
            .Select(line => new
            {
                line.Id,
                line.Quantity,
                line.BaseUnitOfMeasure,
                OccurredAtUtc = line.Shipment.ActualShipAtUtc!.Value
            })
            .ToListAsync(cancellationToken);
        var returns = await context.ReturnReceipts.AsNoTracking()
            .Where(receipt => receipt.ItemId == itemId &&
                receipt.ReturnAuthorization.WarehouseId == warehouseId &&
                receipt.ReceivedAtUtc >= lookbackUtc &&
                receipt.ReceivedAtUtc <= cutoffUtc)
            .OrderByDescending(receipt => receipt.ReceivedAtUtc)
            .ThenByDescending(receipt => receipt.Id)
            .Take(MaximumSourceRowsPerKind + 1)
            .Select(receipt => new
            {
                receipt.Id,
                receipt.Quantity,
                receipt.ReceivedAtUtc,
                UnitOfMeasure = receipt.ReturnLine.ShipmentLine == null
                    ? targetUnit
                    : receipt.ReturnLine.ShipmentLine.BaseUnitOfMeasure
            })
            .ToListAsync(cancellationToken);
        var exceededLimit = shipments.Count > MaximumSourceRowsPerKind ||
            returns.Count > MaximumSourceRowsPerKind;
        var events = shipments.Take(MaximumSourceRowsPerKind)
            .Select(row => new ForecastSourceEvent(
                row.Id, DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
                row.Quantity, row.BaseUnitOfMeasure, ForecastSourceEventKind.Shipment))
            .Concat(returns.Take(MaximumSourceRowsPerKind)
                .Select(row => new ForecastSourceEvent(
                    row.Id, DateTime.SpecifyKind(row.ReceivedAtUtc, DateTimeKind.Utc),
                    row.Quantity, row.UnitOfMeasure, ForecastSourceEventKind.CustomerReturn)))
            .ToArray();
        var sourceText = string.Join("|", events.OrderBy(value => value.OccurredAtUtc)
            .ThenBy(value => value.Kind).ThenBy(value => value.SourceId)
            .Select(value => string.Create(CultureInfo.InvariantCulture,
                $"{value.Kind}:{value.SourceId}:{value.OccurredAtUtc:O}:{value.Quantity}:{value.UnitOfMeasure}")));
        var sourceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceText)))
            .ToLowerInvariant();
        return new SourceEventsResult(events, exceededLimit, sourceFingerprint);
    }

    private Task<ForecastUnitConversion[]> LoadUnitConversionsAsync(
        int itemId,
        CancellationToken cancellationToken) =>
        context.ItemUnitConversions.AsNoTracking()
            .Where(conversion => conversion.ItemId == itemId && conversion.IsActive)
            .Select(conversion => new ForecastUnitConversion(
                conversion.FromUnitOfMeasure,
                conversion.ToUnitOfMeasure,
                conversion.ConversionFactor,
                conversion.Version))
            .ToArrayAsync(cancellationToken);

    private async Task<AvailableSupplyResult> LoadAvailableSupplyAsync(
        int warehouseId,
        int itemId,
        string targetUnit,
        IReadOnlyList<ForecastUnitConversion> conversions,
        CancellationToken cancellationToken)
    {
        var balances = await context.InventoryBalances.AsNoTracking()
            .Where(balance => balance.WarehouseId == warehouseId &&
                balance.ItemId == itemId &&
                balance.InventoryStatus.IsAvailable &&
                (!balance.InventoryStatus.WarehouseId.HasValue ||
                    balance.InventoryStatus.WarehouseId == warehouseId))
            .Select(balance => new { balance.OnHandQuantity, balance.ReservedQuantity, balance.BaseUnitOfMeasure })
            .ToArrayAsync(cancellationToken);
        var totalAvailable = 0m;
        foreach (var balance in balances)
        {
            var converted = ForecastDemandSeriesBuilder.ConvertToTarget(
                balance.OnHandQuantity - balance.ReservedQuantity,
                balance.BaseUnitOfMeasure,
                targetUnit,
                conversions);
            if (converted is null)
            {
                return new AvailableSupplyResult(0m, false, ["available-stock-unit-unconvertible"]);
            }

            totalAvailable += converted.Value;
        }

        var flags = totalAvailable < 0m ? new[] { "negative-available-stock-clamped-to-zero" } : [];
        return new AvailableSupplyResult(Math.Max(0m, totalAvailable), true, flags);
    }

    private async Task<RiskPolicyResult> LoadRiskPolicyAsync(
        int warehouseId,
        int itemId,
        ForecastGranularity granularity,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var policies = await context.InventoryReplenishmentPolicies.AsNoTracking()
            .Where(policy => policy.WarehouseId == warehouseId &&
                policy.ItemId == itemId &&
                policy.IsActive &&
                policy.EffectiveFromUtc <= asOfUtc &&
                (!policy.EffectiveToUtc.HasValue || policy.EffectiveToUtc.Value > asOfUtc))
            .Select(policy => new
            {
                policy.LocationId,
                policy.LeadTimeDays,
                policy.SafetyStockQuantity
            })
            .ToArrayAsync(cancellationToken);
        if (policies.Length == 0)
        {
            return new RiskPolicyResult(0, null, false);
        }

        var warehousePolicies = policies.Where(policy => !policy.LocationId.HasValue).ToArray();
        var selectedPolicies = warehousePolicies.Length > 0 ? warehousePolicies : policies;
        if (selectedPolicies.All(policy => !policy.LeadTimeDays.HasValue))
        {
            return new RiskPolicyResult(0, null, false);
        }

        var leadTimeDays = selectedPolicies
            .Where(policy => policy.LeadTimeDays.HasValue)
            .Select(policy => policy.LeadTimeDays!.Value)
            .DefaultIfEmpty(0)
            .Max();
        var leadTimePeriods = granularity switch
        {
            ForecastGranularity.Daily => leadTimeDays,
            ForecastGranularity.Weekly => (int)Math.Ceiling(leadTimeDays / 7m),
            ForecastGranularity.Monthly => (int)Math.Ceiling(leadTimeDays / 30m),
            _ => 0
        };
        var safetyStock = selectedPolicies.Sum(policy => policy.SafetyStockQuantity);
        return new RiskPolicyResult(leadTimePeriods, safetyStock, true);
    }

    private async Task<StockoutEvidenceResult> LoadStockoutEvidenceAsync(
        int warehouseId,
        int itemId,
        string targetUnit,
        IReadOnlyList<ForecastUnitConversion> conversions,
        string timeZoneId,
        ForecastGranularity granularity,
        IReadOnlyList<ForecastDemandPoint> history,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        if (history.Count == 0)
        {
            return new StockoutEvidenceResult(new HashSet<DateOnly>(), null);
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return new StockoutEvidenceResult(null, "stockout-censoring-time-zone-invalid");
        }

        var availableStatusIds = await context.InventoryStatuses.AsNoTracking()
            .Where(status => status.IsAvailable &&
                (!status.WarehouseId.HasValue || status.WarehouseId == warehouseId))
            .Select(status => status.Id)
            .ToArrayAsync(cancellationToken);
        if (availableStatusIds.Length == 0)
        {
            return new StockoutEvidenceResult(null, "stockout-censoring-status-unknown");
        }

        var current = await context.InventoryBalances.AsNoTracking()
            .Where(balance => balance.WarehouseId == warehouseId &&
                balance.ItemId == itemId && availableStatusIds.Contains(balance.InventoryStatusId))
            .Select(balance => new StockBalanceSnapshot(
                new StockDimensionKey(
                    balance.LocationId, balance.LotId, balance.SerialNumberId, balance.SerialNumber,
                    balance.LicensePlateId, balance.InventoryStatusId, balance.BaseUnitOfMeasure,
                    balance.OwnerKind, balance.InventoryOwnerId, balance.OwnerCodeSnapshot),
                balance.OnHandQuantity - balance.ReservedQuantity,
                balance.BaseUnitOfMeasure))
            .ToArrayAsync(cancellationToken);
        if (current.Length == 0)
        {
            return new StockoutEvidenceResult(null, "stockout-censoring-no-current-balance-snapshot");
        }

        var balances = new Dictionary<StockDimensionKey, decimal>();
        foreach (var snapshot in current)
        {
            var quantity = ForecastDemandSeriesBuilder.ConvertToTarget(
                snapshot.Quantity, snapshot.UnitOfMeasure, targetUnit, conversions);
            if (quantity is null)
            {
                return new StockoutEvidenceResult(null, "stockout-censoring-unit-unconvertible");
            }

            balances[snapshot.Key] = quantity.Value;
        }

        var lookbackUtc = cutoffUtc.AddYears(-10).AddDays(-5);
        var rows = await context.InventoryTransactions.AsNoTracking()
            .Where(transaction => transaction.WarehouseId == warehouseId &&
                transaction.ItemId == itemId &&
                availableStatusIds.Contains(transaction.InventoryStatusId) &&
                transaction.OccurredAtUtc >= lookbackUtc)
            .OrderByDescending(transaction => transaction.OccurredAtUtc)
            .ThenByDescending(transaction => transaction.Id)
            .Take(MaximumLedgerRows + 1)
            .Select(transaction => new StockLedgerSnapshot(
                new StockDimensionKey(
                    transaction.LocationId, transaction.LotId, transaction.SerialNumberId,
                    transaction.SerialNumber, transaction.LicensePlateId, transaction.InventoryStatusId,
                    transaction.BaseUnitOfMeasure, transaction.OwnerKind, transaction.InventoryOwnerId,
                    transaction.OwnerCodeSnapshot),
                transaction.OccurredAtUtc,
                transaction.QuantityDelta - transaction.ReservedQuantityDelta,
                transaction.BaseUnitOfMeasure))
            .ToArrayAsync(cancellationToken);
        if (rows.Length > MaximumLedgerRows)
        {
            return new StockoutEvidenceResult(null, "stockout-censoring-ledger-cap-exceeded");
        }

        var ledger = new List<StockLedgerSnapshot>(rows.Length);
        foreach (var row in rows)
        {
            var delta = ForecastDemandSeriesBuilder.ConvertToTarget(
                row.QuantityDelta, row.UnitOfMeasure, targetUnit, conversions);
            if (delta is null)
            {
                return new StockoutEvidenceResult(null, "stockout-censoring-unit-unconvertible");
            }

            ledger.Add(row with { QuantityDelta = delta.Value });
            balances.TryAdd(row.Key, 0m);
        }

        ledger.Sort((left, right) => right.OccurredAtUtc.CompareTo(left.OccurredAtUtc));
        var stockouts = new HashSet<DateOnly>();
        var cursor = 0;
        while (cursor < ledger.Count && ledger[cursor].OccurredAtUtc > cutoffUtc)
        {
            var row = ledger[cursor++];
            balances[row.Key] -= row.QuantityDelta;
        }

        foreach (var point in history.OrderByDescending(point => point.PeriodStart))
        {
            DateTime boundaryUtc;
            try
            {
                var boundaryLocal = PeriodEndExclusive(point.PeriodStart, granularity);
                boundaryUtc = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(boundaryLocal, DateTimeKind.Unspecified), timeZone);
            }
            catch (ArgumentException)
            {
                return new StockoutEvidenceResult(null, "stockout-censoring-period-boundary-invalid");
            }

            while (cursor < ledger.Count && ledger[cursor].OccurredAtUtc >= boundaryUtc)
            {
                var row = ledger[cursor++];
                if (balances.Values.Sum() <= 0m)
                {
                    var localEvent = TimeZoneInfo.ConvertTimeFromUtc(
                        DateTime.SpecifyKind(row.OccurredAtUtc, DateTimeKind.Utc),
                        timeZone);
                    stockouts.Add(ForecastDemandSeriesBuilder.PeriodStart(
                        DateOnly.FromDateTime(localEvent),
                        granularity));
                }

                balances[row.Key] -= row.QuantityDelta;
            }

            if (balances.Values.Sum() <= 0m)
            {
                stockouts.Add(point.PeriodStart);
            }
        }

        return new StockoutEvidenceResult(stockouts, null);
    }

    private async Task<Result> AuthorizeReadAsync(int? warehouseId, CancellationToken cancellationToken)
    {
        foreach (var permission in new[] { WmsPermissions.ReportsRead, WmsPermissions.InventoryRead })
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                permission, warehouseId, cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization;
            }
        }

        return Result.Success();
    }

    private bool? ResolveStaleness(
        DateOnly? inputPeriodEnd,
        string warehouseTimeZone,
        ForecastGranularity granularity)
    {
        if (!inputPeriodEnd.HasValue)
        {
            return null;
        }

        try
        {
            var latestCompletePeriod = ForecastDemandSeriesBuilder.LatestCompletePeriodStart(
                clock.UtcNow.UtcDateTime,
                warehouseTimeZone,
                granularity);
            return inputPeriodEnd.Value < latestCompletePeriod;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static DateTime PeriodEndExclusive(DateOnly start, ForecastGranularity granularity) =>
        granularity switch
        {
            ForecastGranularity.Daily => start.AddDays(1).ToDateTime(TimeOnly.MinValue),
            ForecastGranularity.Weekly => start.AddDays(7).ToDateTime(TimeOnly.MinValue),
            ForecastGranularity.Monthly => start.AddMonths(1).ToDateTime(TimeOnly.MinValue),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity))
        };

    private static string CombineFingerprints(params string[] fingerprints) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", fingerprints))))
            .ToLowerInvariant();

    private static string[] DeserializeStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return ["stored-quality-metadata-invalid"];
        }
    }

    private static ForecastModelScore[] DeserializeScores(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ForecastModelScore[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
            ? parsed
            : throw new InvalidOperationException("A stored forecast enum value is not recognized.");

    private static TEnum? ParseNullableEnum<TEnum>(string? value)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : ParseEnum<TEnum>(value);

    private readonly record struct WarehouseItemPair(int WarehouseId, int ItemId);
    private sealed record PairBatch(IReadOnlyList<WarehouseItemPair> Pairs, bool HasMore);
    private sealed record PairRecalculationOutcome(bool Created, IReadOnlyList<string> DataQualityFlags);
    private sealed record SourceEventsResult(
        IReadOnlyList<ForecastSourceEvent> Events, bool ExceededLimit, string SourceFingerprint);
    private sealed record AvailableSupplyResult(
        decimal Quantity, bool IsComplete, IReadOnlyList<string> DataQualityFlags);
    private sealed record RiskPolicyResult(
        int LeadTimePeriods, decimal? SafetyStockQuantity, bool IsKnown);
    private sealed record StockoutEvidenceResult(
        IReadOnlySet<DateOnly>? PeriodStarts, string? DataQualityFlag);
    private sealed record StockBalanceSnapshot(
        StockDimensionKey Key, decimal Quantity, string UnitOfMeasure);
    private sealed record StockLedgerSnapshot(
        StockDimensionKey Key, DateTime OccurredAtUtc, decimal QuantityDelta, string UnitOfMeasure);
    private readonly record struct StockDimensionKey(
        int LocationId,
        int? LotId,
        int? SerialNumberId,
        string? SerialNumber,
        int? LicensePlateId,
        int InventoryStatusId,
        string BaseUnitOfMeasure,
        Wms.Domain.Enums.InventoryOwnerKind OwnerKind,
        int? InventoryOwnerId,
        string OwnerCodeSnapshot);
}
