using Wms.Application.Common;

namespace Wms.Application.BrowserTesting;

public enum BrowserSuiteKind
{
    Smoke,
    FullRegression
}

public enum BrowserLocaleDirection
{
    EnglishLtr,
    ArabicRtl
}

public enum BrowserViewportKind
{
    Handheld,
    Phone,
    Tablet,
    Laptop,
    Desktop
}

public sealed record BrowserViewport(
    BrowserViewportKind Kind,
    int Width,
    int Height);

public sealed record BrowserArtifactPolicy(
    bool Screenshot,
    bool Trace,
    bool Video,
    bool ConsoleErrors,
    bool NetworkFailures,
    bool CorrelationId,
    int MaximumArtifactBytes = 50_000_000);

public sealed record BrowserJourneyCase(
    string CaseId,
    string Route,
    BrowserSuiteKind Suite,
    BrowserLocaleDirection Direction,
    BrowserViewport Viewport,
    bool RequiresAuthentication,
    bool RequiresServerOutcomeAssertion,
    bool ExercisesScannerInput,
    bool ExercisesRetryOrReconnect,
    bool UsesFixedSleeps,
    IReadOnlyList<string> ExpectedArtifacts);

public sealed record BrowserQualificationMatrix(
    IReadOnlyList<BrowserJourneyCase> Cases,
    BrowserArtifactPolicy Artifacts,
    bool FailOnUnhandledConsoleError,
    bool PublishFailureArtifacts);

public static class BrowserQualificationPolicy
{
    public const int MaximumCases = 500;
    public static IReadOnlyList<BrowserViewport> RequiredViewports { get; } =
    [
        new(BrowserViewportKind.Handheld, 480, 800),
        new(BrowserViewportKind.Phone, 390, 844),
        new(BrowserViewportKind.Tablet, 768, 1024),
        new(BrowserViewportKind.Laptop, 1366, 768),
        new(BrowserViewportKind.Desktop, 1920, 1080)
    ];

    public static Result Validate(BrowserQualificationMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.Cases is null || matrix.Cases.Count is < 1 or > MaximumCases)
        {
            return Result.Failure(WmsErrors.Validation(
                "browser.matrix_invalid",
                $"The browser matrix must contain between 1 and {MaximumCases} cases."));
        }

        if (!matrix.FailOnUnhandledConsoleError || !matrix.PublishFailureArtifacts ||
            matrix.Artifacts is null ||
            !matrix.Artifacts.Screenshot ||
            !matrix.Artifacts.Trace ||
            !matrix.Artifacts.ConsoleErrors ||
            !matrix.Artifacts.NetworkFailures ||
            !matrix.Artifacts.CorrelationId ||
            matrix.Artifacts.MaximumArtifactBytes is < 1_024 or > 100_000_000)
        {
            return Result.Failure(WmsErrors.Validation(
                "browser.artifacts_incomplete",
                "Browser failures must publish bounded screenshots, traces, console/network evidence, and correlation IDs."));
        }

        if (matrix.Cases.Any(testCase =>
                string.IsNullOrWhiteSpace(testCase.CaseId) ||
                string.IsNullOrWhiteSpace(testCase.Route) ||
                testCase.Viewport.Width <= 0 ||
                testCase.Viewport.Height <= 0 ||
                !testCase.RequiresServerOutcomeAssertion ||
                testCase.UsesFixedSleeps ||
                testCase.ExpectedArtifacts.Count == 0))
        {
            return Result.Failure(WmsErrors.Validation(
                "browser.case_invalid",
                "Every browser case needs a server assertion, artifacts, and deterministic synchronization."));
        }

        if (!matrix.Cases.Any(testCase => testCase.Direction == BrowserLocaleDirection.EnglishLtr) ||
            !matrix.Cases.Any(testCase => testCase.Direction == BrowserLocaleDirection.ArabicRtl) ||
            !matrix.Cases.Any(testCase => testCase.Suite == BrowserSuiteKind.Smoke) ||
            !matrix.Cases.Any(testCase => testCase.Suite == BrowserSuiteKind.FullRegression))
        {
            return Result.Failure(WmsErrors.Validation(
                "browser.coverage_incomplete",
                "The browser matrix must include English/Arabic and smoke/full-regression cases."));
        }

        return Result.Success();
    }

    public static IReadOnlyList<string> RedactFailureValues(
        IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var redacted = new List<string>(values.Count);
        foreach (var pair in values)
        {
            var sensitive = pair.Key.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase);
            redacted.Add($"{pair.Key}={(sensitive ? "[redacted]" : Limit(pair.Value, 500))}");
        }

        return redacted;
    }

    private static string Limit(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength ? value ?? string.Empty : value[..maximumLength];
}

public static class BrowserQualificationMatrixFactory
{
    public static BrowserQualificationMatrix CreateDefault()
    {
        var cases = new List<BrowserJourneyCase>();
        foreach (var direction in Enum.GetValues<BrowserLocaleDirection>())
        {
            foreach (var viewport in BrowserQualificationPolicy.RequiredViewports)
            {
                var caseId = $"dashboard-{direction}-{viewport.Kind}".ToLowerInvariant();
                cases.Add(new BrowserJourneyCase(
                    caseId,
                    "/Dashboard",
                    BrowserSuiteKind.Smoke,
                    direction,
                    viewport,
                    true,
                    true,
                    false,
                    false,
                    false,
                    ["screenshot", "trace", "console", "network", "correlation-id"]));
            }
        }

        cases.Add(new BrowserJourneyCase(
            "scanner-pick-full-regression-ar-rtl",
            "/Picking",
            BrowserSuiteKind.FullRegression,
            BrowserLocaleDirection.ArabicRtl,
            BrowserQualificationPolicy.RequiredViewports[0],
            true,
            true,
            true,
            true,
            false,
            ["screenshot", "trace", "video", "console", "network", "correlation-id"]));
        return new BrowserQualificationMatrix(
            cases,
            new BrowserArtifactPolicy(true, true, true, true, true, true),
            true,
            true);
    }
}
