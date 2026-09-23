using System.Globalization;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Application.Inventory;
using Wms.Application.Recommendations;

namespace Wms.Infrastructure.Recommendations;

public sealed class ReplenishmentRecommendationCommandAdapter(
    IReplenishmentExecutionService replenishmentExecutionService) : IRecommendationCommandAdapter
{
    public RecommendationType Type => RecommendationType.Replenishment;

    public async Task<Result<RecommendationRevalidation>> RevalidateAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        if (!TryGetPolicyId(recommendation, out var policyId))
        {
            return Result.Failure<RecommendationRevalidation>(WmsErrors.Validation(
                "recommendation.action_unsupported",
                "The replenishment proposal does not reference a valid inventory policy."));
        }

        var plans = await replenishmentExecutionService.GenerateAsync(
            new ReplenishmentGenerationQuery(
                recommendation.WarehouseId,
                policyId,
                Limit: 1,
                DryRun: true),
            actorUserId,
            cancellationToken);
        if (plans.IsFailure)
        {
            return plans.ToFailure<RecommendationRevalidation>();
        }

        var plan = plans.Value.Plans.SingleOrDefault(candidate => candidate.PolicyId == policyId);
        if (plan is null)
        {
            return Result.Success(new RecommendationRevalidation(
                string.Empty,
                false,
                "The deterministic replenishment policy no longer produces an eligible signal."));
        }

        return Result.Success(new RecommendationRevalidation(
            plan.SourceStateFingerprint ?? string.Empty,
            plan.Decision == "dry-run-eligible" && plan.PlannedQuantity > 0m,
            plan.Decision));
    }

    public async Task<Result<RecommendationCommandResult>> ExecuteAsync(
        RecommendationRecord recommendation,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        if (!TryGetPolicyId(recommendation, out var policyId))
        {
            return Result.Failure<RecommendationCommandResult>(WmsErrors.Validation(
                "recommendation.action_unsupported",
                "The replenishment proposal does not reference a valid inventory policy."));
        }

        var dryRun = await replenishmentExecutionService.GenerateAsync(
            new ReplenishmentGenerationQuery(
                recommendation.WarehouseId,
                policyId,
                Limit: 1,
                DryRun: true),
            actorUserId,
            cancellationToken);
        if (dryRun.IsFailure)
        {
            return dryRun.ToFailure<RecommendationCommandResult>();
        }

        var currentPlan = dryRun.Value.Plans.SingleOrDefault(candidate => candidate.PolicyId == policyId);
        if (currentPlan is null ||
            currentPlan.Decision != "dry-run-eligible" ||
            currentPlan.PlannedQuantity <= 0m ||
            !string.Equals(
                currentPlan.SourceStateFingerprint,
                recommendation.SourceStateFingerprint,
                StringComparison.Ordinal))
        {
            return Result.Failure<RecommendationCommandResult>(WmsErrors.Conflict(
                "recommendation.stale",
                "The replenishment source, eligible inventory, capacity, status, lot, serial, license-plate, or ownership state changed before work creation."));
        }

        var execution = await replenishmentExecutionService.GenerateAsync(
            new ReplenishmentGenerationQuery(
                recommendation.WarehouseId,
                policyId,
                Limit: 1),
            actorUserId,
            cancellationToken);
        if (execution.IsFailure)
        {
            return execution.ToFailure<RecommendationCommandResult>();
        }

        var plan = execution.Value.Plans.SingleOrDefault(candidate => candidate.PolicyId == policyId);
        if (plan?.WorkId is not int workId ||
            plan.Decision is not ("work-created" or "partial-source-work-created" or "idempotent-work-replay"))
        {
            return Result.Failure<RecommendationCommandResult>(WmsErrors.Conflict(
                "recommendation.execution_blocked",
                "The normal replenishment command did not create or replay eligible work."));
        }

        return Result.Success(new RecommendationCommandResult(
            workId.ToString(CultureInfo.InvariantCulture),
            plan.Decision));
    }

    private static bool TryGetPolicyId(RecommendationRecord recommendation, out int policyId)
    {
        policyId = 0;
        return recommendation.Type == RecommendationType.Replenishment &&
               string.Equals(
                   recommendation.Action.ActionType,
                   "replenishment-policy",
                   StringComparison.Ordinal) &&
               int.TryParse(
                   recommendation.Action.TargetReference,
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out policyId) &&
               policyId > 0;
    }
}
