using FluentAssertions;
using Wms.Application.BulkExchange;
using Wms.Application.Connectors;
using Wms.Application.Context;

namespace Wms.Application.Tests.Security;

public sealed class SecurityBoundaryTests
{
    [Fact]
    public void CorrelationAndCredentialBoundariesDoNotAcceptRawSecrets()
    {
        WmsExecutionIdentifiers.Normalize("corr/with spaces")
            .Should().Be("corrwithspaces");
        var act = () => ConnectorSecurityPolicy.NormalizeCredentialReference(
            "https://example.test?api_key=raw");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CsvExportNeutralizesSpreadsheetFormulaPrefixes()
    {
        BulkCsvSafety.SanitizeForSpreadsheet("=1+1").Should().Be("'=1+1");
        BulkCsvSafety.SanitizeForSpreadsheet("-42").Should().Be("-42");
        BulkCsvSafety.SanitizeForSpreadsheet("@cmd").Should().Be("'@cmd");
    }

    [Fact]
    public void SensitiveConnectorReferencesAreNotReturnedAsSecretMaterial()
    {
        ConnectorSecurityPolicy.Redact("token=hidden")
            .Should().Be("token=[redacted]");
    }
}
