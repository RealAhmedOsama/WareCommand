using FluentAssertions;
using Wms.Application.Connectors;

namespace Wms.Application.Tests.Connectors;

public sealed class ConnectorContractTests
{
    [Fact]
    public void ExternalIdentityAndRunKeysAreDeterministicAndOpaque()
    {
        var first = ConnectorIdentity.BuildExternalKey(17, "order", "SO-100");
        var second = ConnectorIdentity.BuildExternalKey(17, "ORDER", " so-100 ");
        var runKey = ConnectorIdentity.BuildIdempotencyKey(
            4,
            WmsConnectorOperations.OutboundOrders,
            WmsConnectorModes.Pull,
            "cursor-1");

        first.Should().Be(second);
        first.Should().HaveLength(64);
        runKey.Should().StartWith("connector:");
        runKey.Should().NotContain("SO-100");
    }

    [Fact]
    public void MappingAppliesValueMapsAndSafeTransformations()
    {
        var mapped = ConnectorMappingEngine.Apply(
            new Dictionary<string, string?>
            {
                ["externalStatus"] = "open",
                ["sku"] = "  a-1  "
            },
            [
                new ConnectorMappingRule(
                    "externalStatus",
                    "Status",
                    ValueMap: new Dictionary<string, string> { ["open"] = "Open" }),
                new ConnectorMappingRule("sku", "Sku", "upper")
            ]);

        mapped["Status"].Should().Be("Open");
        mapped["Sku"].Should().Be("A-1");
    }

    [Fact]
    public void CredentialReferencesRejectEmbeddedSecrets()
    {
        var act = () => ConnectorSecurityPolicy.NormalizeCredentialReference(
            "https://example.test?token=do-not-store");

        act.Should().Throw<ArgumentException>();
        ConnectorSecurityPolicy.Redact("secret=hidden").Should().Contain("[redacted]");
    }
}
