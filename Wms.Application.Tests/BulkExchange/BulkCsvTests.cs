using FluentAssertions;
using Wms.Application.BulkExchange;

namespace Wms.Application.Tests.BulkExchange;

public sealed class BulkCsvTests
{
    [Fact]
    public void ParserHandlesQuotedCommasAndMultilineFields()
    {
        var result = new BulkCsvParser().Parse(
            "SKU,NAME\nA-1,\"Widget, large\"\nA-2,\"Line one\nLine two\"\n");

        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(2);
        result.Rows[0].Values["NAME"].Should().Be("Widget, large");
        result.Rows[1].Values["NAME"].Should().Be("Line one\nLine two");
    }

    [Fact]
    public void ParserReportsDuplicateHeadersAndColumnShapeErrors()
    {
        var result = new BulkCsvParser().Parse("SKU,SKU,NAME\nA-1,duplicate\n");

        result.Errors.Should().Contain(error => error.Code == "csv.header_duplicate");
        result.Errors.Should().Contain(error => error.Code == "csv.column_count");
    }

    [Fact]
    public void ExportSafetyEscapesFormulaValuesWithoutChangingNormalText()
    {
        BulkCsvSafety.SanitizeForSpreadsheet("=SUM(A1:A2)").Should().Be("'=SUM(A1:A2)");
        BulkCsvSafety.SanitizeForSpreadsheet("normal text").Should().Be("normal text");
        BulkCsvSafety.Escape("a,b").Should().Be("\"a,b\"");
    }
}
