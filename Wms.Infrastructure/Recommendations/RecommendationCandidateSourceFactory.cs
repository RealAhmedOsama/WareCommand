using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Forecasting;
using Wms.Application.Identity;
using Wms.Application.Inbound;
using Wms.Application.Inventory;
using Wms.Application.Outbound;
using Wms.Application.Recommendations;
using Wms.Application.Workforce;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Recommendations;

public sealed class RecommendationCandidateSourceFactory(
    IReplenishmentExecutionService replenishmentExecutionService,
    ISlottingService slottingService,
    IWorkforceService workforceService,
    IInboundExceptionService inboundExceptionService,
    IOutboundExceptionService outboundExceptionService,
    IForecastingService forecastingService,
    IWarehouseAccessService warehouseAccessService,
    IClock clock)
{
    private static readonly TimeSpan ProposalLifetime = TimeSpan.FromDays(7);

    public async Task<Result<IReadOnlyList<RecommendationCandidateSource>>> CreateAsync(
        RecommendationType type,
        RecommendationGenerationRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow.ToUniversalTime();
        return type switch
        {
            RecommendationType.Replenishment => await CreateReplenishmentAsync(
                request, actorUserId, now, cancellationToken),
            RecommendationType.Slotting => await CreateSlottingAsync(
                request, actorUserId, now, cancellationToken),
            RecommendationType.WorkloadPriority => await CreateWorkloadAsync(
                request, actorUserId, cancellationToken),
            RecommendationType.ExceptionResolution => await CreateExceptionResolutionsAsync(
                request, now, cancellationToken),
            RecommendationType.RiskSummary => await CreateRiskSummariesAsync(
                request, now, cancellationToken),
            _ => Result.Failure<IReadOnlyList<RecommendationCandidateSource>>(WmsErrors.Validation(
                "recommendation.type_invalid",
                "The requested recommendation type is not supported."))
        };
    }

    private async Task<Result<IReadOnlyList<RecommendationCandidateSource>>> CreateReplenishmentAsync(
        RecommendationGenerationRequest request,
        string actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var plans = await replenishmentExecutionService.GenerateAsync(
            new ReplenishmentGenerationQuery(request.WarehouseId, Limit: request.Limit, DryRun: true),
            actorUserId,
            cancellationToken);
        if (plans.IsFailure)
        {
            return plans.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
        }

        var candidates = plans.Value.Plans
            .Where(plan => plan.Decision == "dry-run-eligible" &&
                           plan.PlannedQuantity > 0m &&
                           !string.IsNullOrWhiteSpace(plan.SourceStateFingerprint) &&
                           plan.DestinationLocationId.HasValue)
            .Take(request.Limit)
            .Select(plan =>
            {
                var fingerprint = plan.SourceStateFingerprint!;
                var destinationId = plan.DestinationLocationId!.Value;
                return Candidate(
                    RecommendationType.Replenishment,
                    request.WarehouseId,
                    $"replenishment-plan:{plan.PolicyId}:{plan.WarehouseId}:{fingerprint}",
                    fingerprint,
                    "The deterministic replenishment policy identified eligible replenishment work.",
                    new RecommendationAction(
                        "replenishment-policy",
                        plan.PolicyId.ToString(CultureInfo.InvariantCulture),
                        plan.PlannedQuantity,
                        destinationId.ToString(CultureInfo.InvariantCulture)),
                    new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["plannedQuantity"] = plan.PlannedQuantity,
                        ["openWorkQuantity"] = plan.OpenWorkQuantity,
                        ["signalShortfallQuantity"] = plan.SignalShortfallQuantity
                    },
                    0m,
                    plan.PlannedQuantity,
                    now.AddMinutes(-1),
                    now,
                    now);
            })
            .ToArray();
        return Result.Success<IReadOnlyList<RecommendationCandidateSource>>(candidates);
    }

    private async Task<Result<IReadOnlyList<RecommendationCandidateSource>>> CreateSlottingAsync(
        RecommendationGenerationRequest request,
        string actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var analysis = await slottingService.AnalyzeAsync(
            new SlottingAnalysisQuery(
                WarehouseId: request.WarehouseId,
                Limit: request.Limit,
                DryRun: false),
            actorUserId,
            internalExecution: false,
            cancellationToken);
        if (analysis.IsFailure)
        {
            return analysis.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
        }

        var candidates = analysis.Value.Recommendations
            .Where(value => value.Status == SlottingRecommendationStatus.Pending &&
                            !value.WorkId.HasValue &&
                            value.ExpiresAtUtc > now.UtcDateTime)
            .Take(request.Limit)
            .Select(value => Candidate(
                RecommendationType.Slotting,
                value.WarehouseId,
                $"slotting:{value.AnalysisRunKey}:{value.Id}",
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
                    value.ExpiresAtUtc),
                "The deterministic slotting policy identified an eligible location movement.",
                new RecommendationAction(
                    "slotting-recommendation",
                    value.Id.ToString(CultureInfo.InvariantCulture),
                    value.Quantity,
                    value.TargetLocationCode),
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["score"] = value.Score,
                    ["currentScore"] = value.CurrentScore,
                    ["quantity"] = value.Quantity,
                    ["expectedTravelReduction"] = value.ExpectedTravelReduction,
                    ["expectedCongestionReduction"] = value.ExpectedCongestionReduction
                },
                Math.Min(0m, value.ExpectedTravelReduction),
                Math.Max(0m, value.ExpectedTravelReduction),
                new DateTimeOffset(DateTime.SpecifyKind(value.SourcePeriodFromUtc, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(value.SourcePeriodToUtc, DateTimeKind.Utc)),
                now))
            .ToArray();
        return Result.Success<IReadOnlyList<RecommendationCandidateSource>>(candidates);
    }

    private async Task<Result<IReadOnlyList<RecommendationCandidateSource>>> CreateWorkloadAsync(
        RecommendationGenerationRequest request,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        var suggestions = await workforceService.SuggestAsync(
            new WorkforceSuggestionsQuery(request.WarehouseId, Limit: request.Limit),
            actorUserId,
            cancellationToken);
        if (suggestions.IsFailure)
        {
            return suggestions.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
        }

        var candidates = suggestions.Value.Suggestions
            .Take(request.Limit)
            .Select(value =>
            {
                var asOf = new DateTimeOffset(DateTime.SpecifyKind(
                    suggestions.Value.AsOfUtc,
                    DateTimeKind.Utc));
                return Candidate(
                    RecommendationType.WorkloadPriority,
                    value.Work.WarehouseId,
                    $"workforce:{request.WarehouseId}:{value.Work.Id}:{suggestions.Value.AsOfUtc:O}",
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
                        value.IsManualOverride),
                    "The deterministic workforce policy ranked this available task for the current operator.",
                    new RecommendationAction(
                        "work-claim",
                        value.Work.Id.ToString(CultureInfo.InvariantCulture)),
                    new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["score"] = value.Score,
                        ["queuePriority"] = value.QueueId.HasValue ? value.Work.Priority : 0m,
                        ["isFallback"] = value.IsFallback ? 1m : 0m,
                        ["manualOverride"] = value.IsManualOverride ? 1m : 0m
                    },
                    0m,
                    Math.Max(0m, value.Score),
                    asOf,
                    asOf,
                    asOf);
            })
            .ToArray();
        return Result.Success<IReadOnlyList<RecommendationCandidateSource>>(candidates);
    }

    private async Task<Result<IReadOnlyList<RecommendationCandidateSource>>> CreateExceptionResolutionsAsync(
        RecommendationGenerationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var candidates = new List<RecommendationCandidateSource>();
        var remaining = request.Limit;

        if (remaining > 0 && (await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.AdvanceShippingNoticesManage,
                request.WarehouseId,
                cancellationToken)).IsSuccess)
        {
            var inbound = await inboundExceptionService.ListAsync(new InboundExceptionQuery(
                WarehouseId: request.WarehouseId,
                Status: InboundExceptionStatus.UnderReview,
                Page: 1,
                PageSize: remaining), cancellationToken);
            if (inbound.IsFailure)
            {
                return inbound.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
            }

            candidates.AddRange(inbound.Value.Items.Select(value => Candidate(
                RecommendationType.ExceptionResolution,
                value.WarehouseId,
                $"inbound-exception:{value.Id}:revision:{value.Revision}",
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
                    value.LicensePlateId),
                "The inbound exception requires an explicit operator disposition and remains held for review.",
                new RecommendationAction("inbound-exception-hold", $"inbound:{value.Id}"),
                ExceptionFeatures(value.Severity, value.VarianceBaseQuantity),
                0m,
                Math.Abs(value.VarianceBaseQuantity ?? 0m),
                new DateTimeOffset(DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc)),
                now,
                now)));
            remaining = request.Limit - candidates.Count;
        }

        if (remaining > 0 && (await warehouseAccessService.AuthorizeAsync(
                WmsPermissions.SalesOrdersManage,
                request.WarehouseId,
                cancellationToken)).IsSuccess)
        {
            var outbound = await outboundExceptionService.ListAsync(new OutboundExceptionQuery(
                WarehouseId: request.WarehouseId,
                Status: OutboundExceptionStatus.UnderReview,
                Page: 1,
                PageSize: remaining), cancellationToken);
            if (outbound.IsFailure)
            {
                return outbound.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
            }

            candidates.AddRange(outbound.Value.Items.Select(value => Candidate(
                RecommendationType.ExceptionResolution,
                value.WarehouseId,
                $"outbound-exception:{value.Id}:revision:{value.Revision}",
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
                    value.LicensePlateId),
                "The outbound exception requires an explicit operator disposition and remains held for review.",
                new RecommendationAction("outbound-exception-hold", $"outbound:{value.Id}"),
                ExceptionFeatures(value.Severity, value.VarianceBaseQuantity),
                0m,
                Math.Abs(value.VarianceBaseQuantity ?? 0m),
                new DateTimeOffset(DateTime.SpecifyKind(value.CreatedAt, DateTimeKind.Utc)),
                now,
                now)));
        }

        return candidates.Count == 0
            ? Result.Success<IReadOnlyList<RecommendationCandidateSource>>(candidates)
            : Result.Success<IReadOnlyList<RecommendationCandidateSource>>(candidates.Take(request.Limit).ToArray());
    }

    private async Task<Result<IReadOnlyList<RecommendationCandidateSource>>> CreateRiskSummariesAsync(
        RecommendationGenerationRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var runs = await forecastingService.SearchAsync(new ForecastingSearchQuery(
            WarehouseId: request.WarehouseId,
            Page: 1,
            PageSize: Math.Min(request.Limit, ForecastingServicePageLimit)), cancellationToken);
        if (runs.IsFailure)
        {
            return runs.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
        }

        var candidates = new List<RecommendationCandidateSource>();
        foreach (var summary in runs.Value.Items.Where(value =>
                     value.RiskLevel is ForecastRiskLevel.High or ForecastRiskLevel.Medium &&
                     value.IsStale != true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = await forecastingService.GetAsync(summary.Id, cancellationToken);
            if (run.IsFailure)
            {
                return run.ToFailure<IReadOnlyList<RecommendationCandidateSource>>();
            }

            if (run.Value.RiskLevel is not (ForecastRiskLevel.High or ForecastRiskLevel.Medium) ||
                run.Value.IsStale == true)
            {
                continue;
            }

            var features = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["riskLevel"] = (decimal)run.Value.RiskLevel,
                ["horizonPeriods"] = run.Value.HorizonPeriods
            };
            if (run.Value.DaysOfSupply.HasValue)
            {
                features["daysOfSupply"] = run.Value.DaysOfSupply.Value;
            }

            candidates.Add(Candidate(
                RecommendationType.RiskSummary,
                run.Value.WarehouseId,
                $"forecast-run:{run.Value.Id}:{run.Value.InputFingerprint}",
                RecommendationSourceFingerprint.Create(
                    run.Value.Id,
                    run.Value.WarehouseId,
                    run.Value.ItemId,
                    run.Value.Granularity,
                    run.Value.HorizonPeriods,
                    run.Value.ModelVersion,
                    run.Value.InputFingerprint,
                    run.Value.SourceCutoffUtc,
                    run.Value.RiskLevel,
                    run.Value.IsStale),
                $"The deterministic forecast baseline marked this item as {run.Value.RiskLevel.ToString().ToLowerInvariant()} risk; refresh the forecast after approval.",
                new RecommendationAction("forecast-risk-refresh", run.Value.Id.ToString(CultureInfo.InvariantCulture)),
                features,
                0m,
                Math.Max(0m, run.Value.DaysOfSupply ?? 0m),
                new DateTimeOffset(DateTime.SpecifyKind(run.Value.SourceCutoffUtc, DateTimeKind.Utc)),
                now,
                now));
            if (candidates.Count >= request.Limit)
            {
                break;
            }
        }

        return Result.Success<IReadOnlyList<RecommendationCandidateSource>>(candidates);
    }

    private static RecommendationCandidateSource Candidate(
        RecommendationType type,
        int warehouseId,
        string snapshotId,
        string fingerprint,
        string explanation,
        RecommendationAction action,
        IReadOnlyDictionary<string, decimal> features,
        decimal impactLow,
        decimal impactHigh,
        DateTimeOffset sourceFrom,
        DateTimeOffset sourceTo,
        DateTimeOffset generatedAt) =>
        new(
            type,
            warehouseId,
            snapshotId,
            sourceFrom,
            sourceTo,
            fingerprint,
            explanation,
            action,
            features,
            impactLow,
            impactHigh,
            generatedAt,
            generatedAt.Add(ProposalLifetime));

    private static Dictionary<string, decimal> ExceptionFeatures(
        Enum severity,
        decimal? variance)
    {
        var features = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["severity"] = Convert.ToDecimal(severity, CultureInfo.InvariantCulture)
        };
        if (variance.HasValue)
        {
            features["varianceQuantity"] = variance.Value;
        }

        return features;
    }

    private const int ForecastingServicePageLimit = 100;
}

internal static class RecommendationSourceFingerprint
{
    public static string Create(params object?[] values)
    {
        var canonical = string.Join('\u001f', values.Select(value => value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTime date => date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            IFormattable formatted => formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
