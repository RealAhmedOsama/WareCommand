using System.Globalization;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Inventory;
using Wms.Application.Outbound;
using Wms.Application.Recommendations;
using Wms.Application.WarehouseWork;
using Wms.Application.Workforce;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Recommendations;

public sealed class SlottingRecommendationCommandAdapter(
    ISlottingService slottingService,
    IClock clock) : IRecommendationCommandAdapter
{
    public RecommendationType Type => RecommendationType.Slotting;

    public async Task<Result<RecommendationRevalidation>> RevalidateAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetId(recommendation, "slotting-recommendation", out var recommendationId))
        {
            return UnsupportedAction<RecommendationRevalidation>();
        }

        var search = await slottingService.SearchRecommendationsAsync(new SlottingRecommendationQuery(
            WarehouseId: recommendation.WarehouseId,
            IncludeHistorical: true,
            Limit: 500), cancellationToken);
        if (search.IsFailure)
        {
            return search.ToFailure<RecommendationRevalidation>();
        }

        var current = search.Value.SingleOrDefault(value => value.Id == recommendationId);
        if (current is null)
        {
            return Result.Success(new RecommendationRevalidation(string.Empty, false, "The slotting source no longer exists."));
        }

        var fingerprint = Fingerprint(current);
        var eligible = current.Status == SlottingRecommendationStatus.Pending &&
                       current.ExpiresAtUtc > clock.UtcNow.UtcDateTime &&
                       !current.WorkId.HasValue;
        return Result.Success(new RecommendationRevalidation(
            fingerprint,
            eligible,
            eligible ? null : "The slotting source is no longer pending, current, and eligible."));
    }

    public async Task<Result<RecommendationCommandResult>> ExecuteAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetId(recommendation, "slotting-recommendation", out var recommendationId))
        {
            return UnsupportedAction<RecommendationCommandResult>();
        }

        var approved = await slottingService.ApproveAsync(recommendationId, actorUserId, cancellationToken);
        if (approved.IsFailure)
        {
            return approved.ToFailure<RecommendationCommandResult>();
        }

        return approved.Value.Status == SlottingRecommendationStatus.WorkCreated &&
               approved.Value.WorkId.HasValue
            ? Result.Success(new RecommendationCommandResult(
                approved.Value.WorkId.Value.ToString(CultureInfo.InvariantCulture),
                "slotting-work-created"))
            : Result.Failure<RecommendationCommandResult>(WmsErrors.Conflict(
                "recommendation.execution_blocked",
                "The normal slotting command did not create or replay eligible work."));
    }

    private static string Fingerprint(SlottingRecommendationDto value) =>
        RecommendationSourceFingerprint.Create(
            value.Id,
            value.WarehouseId,
            value.ItemId,
            value.Kind,
            value.SourceLocationId,
            value.TargetLocationId,
            value.Quantity,
            value.SourceDataVersion,
            value.AnalysisRunKey,
            value.Status,
            value.Revision,
            value.ExpiresAtUtc);

    private static bool TryGetId(RecommendationRecord recommendation, string actionType, out int id)
    {
        id = 0;
        return recommendation.Type == RecommendationType.Slotting &&
               string.Equals(recommendation.Action.ActionType, actionType, StringComparison.Ordinal) &&
               int.TryParse(recommendation.Action.TargetReference, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
               id > 0;
    }

    private static Result<T> UnsupportedAction<T>() => Result.Failure<T>(WmsErrors.Validation(
        "recommendation.action_unsupported",
        "The slotting proposal does not reference a supported deterministic slotting recommendation."));
}

public sealed class WorkloadPriorityRecommendationCommandAdapter(
    IWorkforceService workforceService) : IRecommendationCommandAdapter
{
    public RecommendationType Type => RecommendationType.WorkloadPriority;

    public async Task<Result<RecommendationRevalidation>> RevalidateAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetId(recommendation, out var workId))
        {
            return UnsupportedAction<RecommendationRevalidation>();
        }

        var current = await GetCurrentSuggestionAsync(recommendation.WarehouseId, workId, actorUserId, cancellationToken);
        if (current.IsFailure)
        {
            return current.ToFailure<RecommendationRevalidation>();
        }

        return current.Value is null
            ? Result.Success(new RecommendationRevalidation(string.Empty, false, "The task is no longer in the deterministic eligible queue."))
            : Result.Success(new RecommendationRevalidation(Fingerprint(current.Value), true));
    }

    public async Task<Result<RecommendationCommandResult>> ExecuteAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetId(recommendation, out var workId))
        {
            return UnsupportedAction<RecommendationCommandResult>();
        }

        var claim = await workforceService.ClaimAsync(
            workId,
            new WarehouseWorkClaimInput($"recommendation:{recommendation.RecommendationId}"),
            actorUserId,
            cancellationToken);
        return claim.IsFailure
            ? claim.ToFailure<RecommendationCommandResult>()
            : Result.Success(new RecommendationCommandResult(
                claim.Value.Id.ToString(CultureInfo.InvariantCulture),
                "work-claimed"));
    }

    private async Task<Result<WorkSuggestionDto?>> GetCurrentSuggestionAsync(
        int warehouseId,
        int workId,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var suggestions = await workforceService.SuggestAsync(
            new WorkforceSuggestionsQuery(warehouseId, Limit: 200),
            actorUserId,
            cancellationToken);
        if (suggestions.IsFailure)
        {
            return suggestions.ToFailure<WorkSuggestionDto?>();
        }

        return Result.Success(suggestions.Value.Suggestions.SingleOrDefault(value => value.Work.Id == workId));
    }

    private static string Fingerprint(WorkSuggestionDto value) =>
        RecommendationSourceFingerprint.Create(
            value.Work.Id,
            value.Work.WarehouseId,
            value.Work.Type,
            value.Work.Status,
            value.Work.Revision,
            value.Work.AssignedUserId,
            value.QueueId,
            value.QueueCode,
            value.Score,
            value.ScoringBreakdown,
            value.IsFallback,
            value.IsManualOverride);

    private static bool TryGetId(RecommendationRecord recommendation, out int workId)
    {
        workId = 0;
        return recommendation.Type == RecommendationType.WorkloadPriority &&
               string.Equals(recommendation.Action.ActionType, "work-claim", StringComparison.Ordinal) &&
               int.TryParse(recommendation.Action.TargetReference, NumberStyles.None, CultureInfo.InvariantCulture, out workId) &&
               workId > 0;
    }

    private static Result<T> UnsupportedAction<T>() => Result.Failure<T>(WmsErrors.Validation(
        "recommendation.action_unsupported",
        "The workload proposal does not reference a supported eligible warehouse task."));
}

public sealed class ExceptionResolutionRecommendationCommandAdapter(
    IInboundExceptionService inboundExceptionService,
    IOutboundExceptionService outboundExceptionService) : IRecommendationCommandAdapter
{
    public RecommendationType Type => RecommendationType.ExceptionResolution;

    public async Task<Result<RecommendationRevalidation>> RevalidateAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetTarget(recommendation, out var direction, out var exceptionId))
        {
            return UnsupportedAction<RecommendationRevalidation>();
        }

        if (direction == "inbound")
        {
            var current = await inboundExceptionService.GetAsync(exceptionId, cancellationToken);
            if (current.IsFailure)
            {
                return current.ToFailure<RecommendationRevalidation>();
            }

            var eligible = current.Value.Status == InboundExceptionStatus.UnderReview && !current.Value.IsTerminal;
            return Result.Success(new RecommendationRevalidation(
                Fingerprint(current.Value),
                eligible,
                eligible ? null : "The inbound exception is no longer under review."));
        }

        var outbound = await outboundExceptionService.GetAsync(exceptionId, cancellationToken);
        if (outbound.IsFailure)
        {
            return outbound.ToFailure<RecommendationRevalidation>();
        }

        var outboundEligible = outbound.Value.Status == OutboundExceptionStatus.UnderReview && !outbound.Value.IsTerminal;
        return Result.Success(new RecommendationRevalidation(
            Fingerprint(outbound.Value),
            outboundEligible,
            outboundEligible ? null : "The outbound exception is no longer under review."));
    }

    public async Task<Result<RecommendationCommandResult>> ExecuteAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetTarget(recommendation, out var direction, out var exceptionId))
        {
            return UnsupportedAction<RecommendationCommandResult>();
        }

        const string reason = "Governed recommendation approved; retain the exception on hold for operator review.";
        if (direction == "inbound")
        {
            var resolution = await inboundExceptionService.ResolveAsync(
                exceptionId,
                new InboundExceptionResolutionInput(InboundExceptionResolution.Hold, reason),
                actorUserId,
                cancellationToken);
            return resolution.IsFailure
                ? resolution.ToFailure<RecommendationCommandResult>()
                : Result.Success(new RecommendationCommandResult(
                    exceptionId.ToString(CultureInfo.InvariantCulture),
                    "inbound-exception-held",
                    "inbound-exception"));
        }

        var outbound = await outboundExceptionService.ResolveAsync(
            exceptionId,
            $"recommendation:{recommendation.RecommendationId}",
            new OutboundExceptionResolutionInput(OutboundExceptionResolution.HoldOrder, reason),
            actorUserId,
            cancellationToken);
        return outbound.IsFailure
            ? outbound.ToFailure<RecommendationCommandResult>()
            : Result.Success(new RecommendationCommandResult(
                exceptionId.ToString(CultureInfo.InvariantCulture),
                "outbound-exception-held",
                "outbound-exception"));
    }

    private static string Fingerprint(InboundExceptionDto value) =>
        RecommendationSourceFingerprint.Create(
            "inbound",
            value.Id,
            value.WarehouseId,
            value.Code,
            value.Severity,
            value.Status,
            value.Revision,
            value.ExpectedBaseQuantity,
            value.ActualBaseQuantity,
            value.VarianceBaseQuantity,
            value.ReceiptId,
            value.AdvanceShippingNoticeId,
            value.WarehouseWorkId,
            value.ItemId,
            value.LicensePlateId);

    private static string Fingerprint(OutboundExceptionDto value) =>
        RecommendationSourceFingerprint.Create(
            "outbound",
            value.Id,
            value.WarehouseId,
            value.Code,
            value.Severity,
            value.Status,
            value.Revision,
            value.ExpectedBaseQuantity,
            value.ActualBaseQuantity,
            value.VarianceBaseQuantity,
            value.SalesOrderId,
            value.ShipmentId,
            value.WarehouseWorkId,
            value.ItemId,
            value.LicensePlateId);

    private static bool TryGetTarget(RecommendationRecord recommendation, out string direction, out int exceptionId)
    {
        direction = string.Empty;
        exceptionId = 0;
        if (recommendation.Type != RecommendationType.ExceptionResolution)
        {
            return false;
        }

        var action = recommendation.Action.ActionType;
        var target = recommendation.Action.TargetReference;
        if (action == "inbound-exception-hold" && target.StartsWith("inbound:", StringComparison.Ordinal))
        {
            direction = "inbound";
            return int.TryParse(target.AsSpan("inbound:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out exceptionId) && exceptionId > 0;
        }

        if (action == "outbound-exception-hold" && target.StartsWith("outbound:", StringComparison.Ordinal))
        {
            direction = "outbound";
            return int.TryParse(target.AsSpan("outbound:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out exceptionId) && exceptionId > 0;
        }

        return false;
    }

    private static Result<T> UnsupportedAction<T>() => Result.Failure<T>(WmsErrors.Validation(
        "recommendation.action_unsupported",
        "The exception proposal does not reference a supported deterministic hold action."));
}

public sealed class RiskSummaryRecommendationCommandAdapter(
    IForecastingService forecastingService) : IRecommendationCommandAdapter
{
    public RecommendationType Type => RecommendationType.RiskSummary;

    public async Task<Result<RecommendationRevalidation>> RevalidateAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRunId(recommendation, out var runId))
        {
            return UnsupportedAction<RecommendationRevalidation>();
        }

        var run = await forecastingService.GetAsync(runId, cancellationToken);
        if (run.IsFailure)
        {
            return run.ToFailure<RecommendationRevalidation>();
        }

        var eligible = run.Value.WarehouseId == recommendation.WarehouseId &&
                       run.Value.IsStale != true &&
                       run.Value.RiskLevel is ForecastRiskLevel.High or ForecastRiskLevel.Medium;
        return Result.Success(new RecommendationRevalidation(
            Fingerprint(run.Value),
            eligible,
            eligible ? null : "The forecast run is stale or no longer has a supported risk signal."));
    }

    public async Task<Result<RecommendationCommandResult>> ExecuteAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetRunId(recommendation, out var runId))
        {
            return UnsupportedAction<RecommendationCommandResult>();
        }

        var source = await forecastingService.GetAsync(runId, cancellationToken);
        if (source.IsFailure)
        {
            return source.ToFailure<RecommendationCommandResult>();
        }

        var recalculated = await forecastingService.RecalculateAsync(new ForecastingRecalculationQuery(
            source.Value.WarehouseId,
            source.Value.ItemId,
            source.Value.Granularity,
            source.Value.HorizonPeriods), cancellationToken);
        if (recalculated.IsFailure)
        {
            return recalculated.ToFailure<RecommendationCommandResult>();
        }

        var latest = await forecastingService.SearchAsync(new ForecastingSearchQuery(
            source.Value.WarehouseId,
            source.Value.ItemId,
            Page: 1,
            PageSize: 100), cancellationToken);
        if (latest.IsFailure)
        {
            return latest.ToFailure<RecommendationCommandResult>();
        }

        var refreshed = latest.Value.Items.FirstOrDefault(value =>
            value.Granularity == source.Value.Granularity &&
            value.HorizonPeriods == source.Value.HorizonPeriods);
        return refreshed is null
            ? Result.Failure<RecommendationCommandResult>(WmsErrors.Conflict(
                "recommendation.execution_blocked",
                "The deterministic forecast command did not create or replay a current forecast run."))
            : Result.Success(new RecommendationCommandResult(
                refreshed.Id.ToString(CultureInfo.InvariantCulture),
                "forecast-recalculated",
                "forecast-run"));
    }

    private static string Fingerprint(ForecastRunDto value) =>
        RecommendationSourceFingerprint.Create(
            value.Id,
            value.WarehouseId,
            value.ItemId,
            value.Granularity,
            value.HorizonPeriods,
            value.ModelVersion,
            value.InputFingerprint,
            value.SourceCutoffUtc,
            value.RiskLevel,
            value.IsStale);

    private static bool TryGetRunId(RecommendationRecord recommendation, out int runId)
    {
        runId = 0;
        return recommendation.Type == RecommendationType.RiskSummary &&
               string.Equals(recommendation.Action.ActionType, "forecast-risk-refresh", StringComparison.Ordinal) &&
               int.TryParse(recommendation.Action.TargetReference, NumberStyles.None, CultureInfo.InvariantCulture, out runId) &&
               runId > 0;
    }

    private static Result<T> UnsupportedAction<T>() => Result.Failure<T>(WmsErrors.Validation(
        "recommendation.action_unsupported",
        "The risk summary does not reference a supported deterministic forecast run."));
}
