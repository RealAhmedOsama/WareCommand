using System.Text;
using Wms.Application.Devices;
using Wms.Application.Labels;
using Wms.Domain.Enums;

namespace Wms.Application.Tests.Labels;

public sealed class LabelTemplateEngineTests
{
    [Fact]
    public void ZplTemplateBindsDeclaredFieldsAndValidatesGs1AndSscc()
    {
        var definition = new WmsLabelTemplateDefinition
        {
            Name = "receipt-label",
            Version = 2,
            DocumentType = WmsLabelDocumentType.Receipt,
            Format = WmsPrintFormat.Zpl,
            Body = "SKU {{sku}}\nLOT {{lot}}",
            Fields =
            [
                new WmsLabelFieldDefinition { Key = "sku", Kind = WmsLabelFieldKind.ProductCode },
                new WmsLabelFieldDefinition { Key = "lot", Kind = WmsLabelFieldKind.Text }
            ],
            Barcodes =
            [
                new WmsLabelBarcodeDefinition
                {
                    Name = "sscc",
                    SourceKey = "sscc",
                    Symbology = BarcodeSymbology.Sscc
                },
                new WmsLabelBarcodeDefinition
                {
                    Name = "gs1",
                    SourceKey = "gs1",
                    Symbology = BarcodeSymbology.Gs1
                }
            ]
        };

        definition = definition with
        {
            Fields =
            [
                .. definition.Fields,
                new WmsLabelFieldDefinition { Key = "sscc", Kind = WmsLabelFieldKind.Sscc },
                new WmsLabelFieldDefinition { Key = "gs1", Kind = WmsLabelFieldKind.Gs1 }
            ]
        };

        var result = WmsLabelTemplateEngine.Render(
            definition,
            new Dictionary<string, string?>
            {
                ["sku"] = "00012345678905",
                ["lot"] = "LOT-A",
                ["sscc"] = "000123456789012343",
                ["gs1"] = "(01)00012345678905(17)251231(10)LOT-A(21)SER-9"
            });

        result.IsSuccess.Should().BeTrue(result.Error);
        var zpl = Encoding.UTF8.GetString(result.Value.Payload);
        zpl.Should().StartWith("^XA");
        zpl.Should().Contain("^BCN");
        zpl.Should().Contain("SKU 00012345678905");
        zpl.Should().EndWith("^XZ\n");
    }

    [Fact]
    public void InvalidPlaceholderAndExecutableTemplateAreRejected()
    {
        var definition = new WmsLabelTemplateDefinition
        {
            Name = "unsafe",
            Body = "<script>{{secret}}</script>",
            Fields =
            [
                new WmsLabelFieldDefinition { Key = "name" }
            ]
        };

        var errors = WmsLabelTemplateValidator.Validate(definition);

        errors.Should().ContainKey(nameof(WmsLabelTemplateDefinition.Body));
        errors[nameof(WmsLabelTemplateDefinition.Body)]
            .Should().Contain(message => message.Contains("Executable", StringComparison.Ordinal));
        errors[nameof(WmsLabelTemplateDefinition.Body)]
            .Should().Contain(message => message.Contains("not declared", StringComparison.Ordinal));
    }

    [Fact]
    public void SecretLikeFieldsAreRejectedEvenWhenThePlaceholderIsDeclared()
    {
        var definition = new WmsLabelTemplateDefinition
        {
            Name = "secret-field",
            Body = "{{apiToken}}",
            Fields =
            [
                new WmsLabelFieldDefinition { Key = "apiToken" }
            ]
        };

        var errors = WmsLabelTemplateValidator.Validate(definition);

        errors.ContainsKey(nameof(WmsLabelTemplateDefinition.Fields)).Should().BeFalse();
        errors.Should().ContainKey("Fields.apiToken");
        errors["Fields.apiToken"]
            .Should().Contain(message => message.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public void ArabicPdfExposesRtlBrowserStrategyAndKeepsPdfPayloadValid()
    {
        var definition = new WmsLabelTemplateDefinition
        {
            Name = "location-ar",
            Language = "ar-SA",
            Format = WmsPrintFormat.Pdf,
            Body = "{{name}}",
            Fields =
            [
                new WmsLabelFieldDefinition { Key = "name" }
            ]
        };

        var result = WmsLabelTemplateEngine.Render(
            definition,
            new Dictionary<string, string?> { ["name"] = "ممر الاستلام" });

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Payload.Should().StartWith(Encoding.ASCII.GetBytes("%PDF-1.4"));
        result.Value.PreferBrowserPdf.Should().BeTrue();
        result.Value.BrowserHtml.Should().Contain("lang=\"ar-SA\"");
        result.Value.BrowserHtml.Should().Contain("dir=\"rtl\"");
        result.Value.BrowserHtml.Should().Contain("ممر الاستلام");
    }

    [Fact]
    public void UnsafeRuntimeValueCannotInjectZplCommands()
    {
        var definition = new WmsLabelTemplateDefinition
        {
            Name = "safe-value",
            Format = WmsPrintFormat.Zpl,
            Body = "{{value}}",
            Fields =
            [
                new WmsLabelFieldDefinition { Key = "value" }
            ]
        };

        var result = WmsLabelTemplateEngine.Render(
            definition,
            new Dictionary<string, string?> { ["value"] = "A^FS~DG" });

        result.IsSuccess.Should().BeTrue(result.Error);
        var zpl = Encoding.UTF8.GetString(result.Value.Payload);
        zpl.Should().NotContain("A^FS~DG");
        zpl.Should().Contain("A FS DG");
    }
}
