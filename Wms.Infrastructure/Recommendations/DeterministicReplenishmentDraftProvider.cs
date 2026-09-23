using System.Globalization;
using Wms.Application.Common;
using Wms.Application.Recommendations;

namespace Wms.Infrastructure.Recommendations;

/// <summary>
/// Local, zero-network provider. It exposes the deterministic replenishment
/// engine's plan as an advisory proposal without calling a paid model.
/// </summary>
public sealed class DeterministicReplenishmentDraftProvider : IRecommendationDraftProvider
{
    public const string ProviderName = "deterministic-wms";

    public string Name => ProviderName;

    public bool IsAvailable => true;

    public Task<Result<RecommendationDraft>> CreateReplenishmentDraftAsync(
        ReplenishmentRecommendationSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var plan = source.Plan;
        if (string.IsNullOrWhiteSpace(plan.SourceStateFingerprint) ||
            plan.PlannedQuantity <= 0m ||
            !plan.DestinationLocationId.HasValue)
        {
            return Task.FromResult(Result.Failure<RecommendationDraft>(WmsErrors.Conflict(
                "recommendation.plan_not_eligible",
                "The deterministic replenishment plan is no longer eligible for proposal generation.")));
        }

        var now = source.GeneratedAtUtc.ToUniversalTime();
        var policyId = plan.PolicyId.ToString(CultureInfo.InvariantCulture);
        var destinationId = plan.DestinationLocationId.Value.ToString(CultureInfo.InvariantCulture);
        var draft = new RecommendationDraft(
            RecommendationType.Replenishment,
            plan.WarehouseId,
            $"replenishment-plan:{plan.PolicyId}:{plan.WarehouseId}:{plan.SourceStateFingerprint}",
            now.AddMinutes(-1),
            now,
            plan.SourceStateFingerprint,
            Name,
            "rules-v1",
            "inventory-replenishment-policy/v1",
            1m,
            $"Deterministic replenishment policy {plan.PolicyId} can move {plan.PlannedQuantity.ToString(CultureInfo.InvariantCulture)} units of {plan.ItemSku} to location {destinationId}.",
            new RecommendationAction("replenishment-policy", policyId, plan.PlannedQuantity, destinationId),
            new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["plannedQuantity"] = plan.PlannedQuantity,
                ["openWorkQuantity"] = plan.OpenWorkQuantity,
                ["signalShortfallQuantity"] = plan.SignalShortfallQuantity
            },
            0m,
            plan.PlannedQuantity,
            now,
            source.ExpiresAtUtc.ToUniversalTime());
        return Task.FromResult(Result.Success(draft));
    }
}
