using FluentAssertions;
using Wms.Application.B2bDocuments;

namespace Wms.Application.Tests.B2bDocuments;

public sealed class B2bDocumentContractTests
{
    [Fact]
    public void ExternalControlIdentityIsStableAcrossFormatting()
    {
        var first = B2bDocumentIdentity.BuildExternalKey(4, " i-100 ", "d-100");
        var second = B2bDocumentIdentity.BuildExternalKey(4, "I-100", "D-100");

        first.Should().Be(second);
        first.Should().HaveLength(64);
    }

    [Fact]
    public void CanonicalPayloadSerializationRoundTripsWithoutExposingProviderEnvelope()
    {
        var fields = new Dictionary<string, string?>
        {
            ["purchaseOrderNumber"] = "PO-1",
            ["status"] = "Open"
        };

        var json = B2bCanonicalPayload.Serialize(fields);
        var roundTrip = B2bCanonicalPayload.Deserialize(json);

        roundTrip.Should().BeEquivalentTo(fields);
        json.Should().NotContain("ISA");
    }

    [Fact]
    public void SupportedDocumentCatalogIncludesRequiredWarehouseFlows()
    {
        WmsB2bDocumentTypes.IsKnown(WmsB2bDocumentTypes.PurchaseOrder).Should().BeTrue();
        WmsB2bDocumentTypes.IsKnown(WmsB2bDocumentTypes.AdvanceShippingNotice).Should().BeTrue();
        WmsB2bDocumentTypes.IsKnown(WmsB2bDocumentTypes.ShipmentConfirmation).Should().BeTrue();
        WmsB2bDocumentTypes.IsKnown("customer-specific-document").Should().BeFalse();
    }
}
