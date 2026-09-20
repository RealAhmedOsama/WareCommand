using Wms.Domain.Enums;
using Wms.Domain.Identification;

namespace Wms.Domain.Tests.Identification;

public sealed class BarcodeParserTests
{
    [Theory]
    [InlineData("0123456789012", BarcodeSymbology.Ean13)]
    [InlineData("012345678905", BarcodeSymbology.UpcA)]
    [InlineData("00012345678905", BarcodeSymbology.Gtin14)]
    public void ProductCodes_ValidateCheckDigitsWithoutGuessingInternalNumericValues(
        string value,
        BarcodeSymbology expectedSymbology)
    {
        var internalValue = BarcodeParser.Parse(value);
        var productCode = BarcodeParser.ParseProductCode(value);

        internalValue.Symbology.Should().Be(BarcodeSymbology.Internal);
        productCode.Symbology.Should().Be(expectedSymbology);
        productCode.IsCheckDigitValidated.Should().BeTrue();
        productCode.NormalizedValue.Should().Be(value);
    }

    [Fact]
    public void ProductCodes_RejectInvalidCheckDigits()
    {
        var act = () => BarcodeParser.ParseProductCode("00012345678904");

        act.Should().Throw<ArgumentException>().WithMessage("*check digit*");
    }

    [Fact]
    public void Gs1_ParsesStructuredTraceabilityFields()
    {
        var payload = Gs1Parser.Parse(
            "(01)00012345678905(17)251231(10)LOT-A(21)SER-9(30)12");

        payload.Gtin.Should().Be("00012345678905");
        payload.Lot.Should().Be("LOT-A");
        payload.Serial.Should().Be("SER-9");
        payload.ExpiryDate.Should().Be(new DateOnly(2025, 12, 31));
        payload.Quantity.Should().Be(12m);
        payload.ApplicationIdentifiers.Should().ContainKey("17");
    }

    [Fact]
    public void Gs1_RawFnc1Payload_ParsesVariableFieldsAndSscc()
    {
        var payload = Gs1Parser.Parse(
            "00" + "000123456789012343" + "10" + "LOT-A" + Gs1Parser.GroupSeparator + "21SER-9");

        payload.Sscc.Should().Be("000123456789012343");
        payload.Lot.Should().Be("LOT-A");
        payload.Serial.Should().Be("SER-9");
    }

    [Theory]
    [InlineData("(01)00012345678904")]
    [InlineData("(17)251332")]
    [InlineData("(99)UNKNOWN")]
    public void Gs1_RejectsInvalidCheckDigitsDatesAndUnconfiguredAis(string value)
    {
        var act = () => Gs1Parser.Parse(value);

        act.Should().Throw<ArgumentException>();
    }
}
