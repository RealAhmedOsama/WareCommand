using FluentAssertions;
using Wms.Application.BrowserTesting;

namespace Wms.Application.Tests.BrowserTesting;

public sealed class BrowserQualificationMatrixTests
{
    [Fact]
    public void Default_matrix_covers_both_directions_viewports_and_suites()
    {
        var matrix = BrowserQualificationMatrixFactory.CreateDefault();

        BrowserQualificationPolicy.Validate(matrix).IsSuccess.Should().BeTrue();
        matrix.Cases.Should().HaveCount(11);
        matrix.Cases.Select(testCase => testCase.Direction)
            .Should().Contain([BrowserLocaleDirection.EnglishLtr, BrowserLocaleDirection.ArabicRtl]);
        matrix.Cases.Select(testCase => testCase.Viewport.Kind)
            .Should().Contain(BrowserQualificationPolicy.RequiredViewports.Select(viewport => viewport.Kind));
    }

    [Fact]
    public void Fixed_sleep_and_missing_server_assertion_are_rejected()
    {
        var matrix = BrowserQualificationMatrixFactory.CreateDefault() with
        {
            Cases = [BrowserQualificationMatrixFactory.CreateDefault().Cases[0] with
            {
                UsesFixedSleeps = true,
                RequiresServerOutcomeAssertion = false
            }]
        };

        var result = BrowserQualificationPolicy.Validate(matrix);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("browser.case_invalid");
    }

    [Fact]
    public void Failure_values_redact_credentials_and_bound_diagnostics()
    {
        var values = BrowserQualificationPolicy.RedactFailureValues(
            new Dictionary<string, string?>
            {
                ["authorization"] = "Bearer secret-value",
                ["console"] = new string('x', 700)
            });

        values.Should().Contain(value => value == "authorization=[redacted]");
        values.Single(value => value.StartsWith("console=", StringComparison.Ordinal))
            .Length.Should().Be(508);
    }

    [Fact]
    public void Full_regression_scanner_case_requires_retry_and_artifact_evidence()
    {
        var scannerCase = BrowserQualificationMatrixFactory.CreateDefault().Cases
            .Single(testCase => testCase.CaseId == "scanner-pick-full-regression-ar-rtl");

        scannerCase.ExercisesScannerInput.Should().BeTrue();
        scannerCase.ExercisesRetryOrReconnect.Should().BeTrue();
        scannerCase.ExpectedArtifacts.Should().Contain("video");
    }
}
