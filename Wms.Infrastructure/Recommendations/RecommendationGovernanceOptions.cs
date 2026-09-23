namespace Wms.Infrastructure.Recommendations;

public sealed class RecommendationGovernanceOptions
{
    public const string SectionName = "Recommendations";

    public bool Enabled { get; set; }

    public bool KillSwitchEnabled { get; set; } = true;

    public bool ProviderAvailable { get; set; } = true;

    public bool ShadowMode { get; set; } = true;

    public string ProviderName { get; set; } = DeterministicReplenishmentDraftProvider.ProviderName;

    public int MaximumProposalsPerRequest { get; set; } = 50;

    public int ProviderTimeoutSeconds { get; set; } = 10;
}
