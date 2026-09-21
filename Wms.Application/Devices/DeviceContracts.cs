using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Devices;

public enum WmsScanSource
{
    KeyboardWedge = 1,
    Camera = 2,
    NativeAdapter = 3,
    Manual = 4
}

public enum WmsScanDisposition
{
    Accepted = 1,
    Incomplete = 2,
    Duplicate = 3,
    Rejected = 4
}

public enum WmsScaleCaptureDisposition
{
    Accepted = 1,
    AwaitingStableWeight = 2,
    Rejected = 3
}

public enum WmsPrintFormat
{
    Pdf = 1,
    Zpl = 2
}

public enum WmsPrintTransport
{
    BrowserPdf = 1,
    NetworkZpl = 2,
    LocalAdapter = 3,
    NativeDocument = 4
}

/// <summary>
/// A bounded, vendor-neutral scanner profile. Hardware adapters translate
/// their own configuration into this contract before values reach a workflow.
/// </summary>
public sealed record WmsScannerProfile
{
    public required string Name { get; init; }

    public int MinimumLength { get; init; } = 3;

    public int MaximumLength { get; init; } = 50;

    public string? Prefix { get; init; }

    public string Terminator { get; init; } = "\r";

    public TimeSpan InterCharacterTimeout { get; init; } = TimeSpan.FromMilliseconds(125);

    public TimeSpan DuplicateSuppressionWindow { get; init; } = TimeSpan.FromMilliseconds(750);

    public IReadOnlyDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100)
        {
            Add(errors, nameof(Name), "Scanner profile name is required and must be at most 100 characters.");
        }

        if (MinimumLength is < 1 or > 200)
        {
            Add(errors, nameof(MinimumLength), "Scanner minimum length must be between 1 and 200.");
        }

        if (MaximumLength is < 1 or > 500 || MaximumLength < MinimumLength)
        {
            Add(errors, nameof(MaximumLength), "Scanner maximum length must be between 1 and 500 and not less than the minimum.");
        }

        if (Prefix?.Length > 20)
        {
            Add(errors, nameof(Prefix), "Scanner prefix must be at most 20 characters.");
        }

        if (Terminator.Length > 10 || Terminator.Any(character => !char.IsControl(character)))
        {
            Add(errors, nameof(Terminator), "Scanner terminator must contain only control characters and be at most 10 characters.");
        }

        if (InterCharacterTimeout <= TimeSpan.Zero || InterCharacterTimeout > TimeSpan.FromSeconds(5))
        {
            Add(errors, nameof(InterCharacterTimeout), "Scanner inter-character timeout must be greater than zero and at most five seconds.");
        }

        if (DuplicateSuppressionWindow < TimeSpan.Zero || DuplicateSuppressionWindow > TimeSpan.FromMinutes(5))
        {
            Add(errors, nameof(DuplicateSuppressionWindow), "Scanner duplicate window must be between zero and five minutes.");
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var values))
        {
            values = [];
            errors[key] = values;
        }

        values.Add(message);
    }
}

public sealed record WmsRawScanInput(
    string Value,
    WmsScanSource Source,
    string StationCode,
    DateTimeOffset CapturedAtUtc,
    BarcodeSymbology Symbology = BarcodeSymbology.Unknown);

public sealed record WmsNormalizedScan(
    string ScanId,
    string Value,
    WmsScanSource Source,
    string StationCode,
    DateTimeOffset CapturedAtUtc,
    BarcodeSymbology Symbology);

public sealed record WmsScanNormalizationResult(
    WmsScanDisposition Disposition,
    WmsNormalizedScan? Scan = null,
    string? ErrorCode = null,
    string? Message = null)
{
    public bool IsAccepted => Disposition == WmsScanDisposition.Accepted;
}

public sealed record WmsScanBufferResult(
    WmsScanDisposition Disposition,
    WmsNormalizedScan? Scan = null,
    string? ErrorCode = null,
    string? Message = null);

public sealed record WmsScaleProfile
{
    public required string Unit { get; init; }

    public int RequiredConsecutiveStableSamples { get; init; } = 3;

    public decimal MaximumDeviation { get; init; } = 0.01m;

    public int DecimalPlaces { get; init; } = 3;

    public bool AllowNegative { get; init; }

    public bool ManualFallbackAllowed { get; init; } = true;

    public IReadOnlyDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(Unit) || Unit.Trim().Length > 20)
        {
            Add(errors, nameof(Unit), "Scale unit is required and must be at most 20 characters.");
        }

        if (RequiredConsecutiveStableSamples is < 1 or > 100)
        {
            Add(errors, nameof(RequiredConsecutiveStableSamples), "Stable sample count must be between 1 and 100.");
        }

        if (MaximumDeviation < 0 || MaximumDeviation > 1_000_000m)
        {
            Add(errors, nameof(MaximumDeviation), "Scale maximum deviation must be non-negative and bounded.");
        }

        if (DecimalPlaces is < 0 or > 6)
        {
            Add(errors, nameof(DecimalPlaces), "Scale decimal places must be between 0 and 6.");
        }

        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var values))
        {
            values = [];
            errors[key] = values;
        }

        values.Add(message);
    }
}

public sealed record WmsScaleReading(
    decimal Value,
    string Unit,
    bool IsStable,
    DateTimeOffset CapturedAtUtc);

public sealed record WmsStableWeight(
    decimal Value,
    string Unit,
    int SampleCount,
    DateTimeOffset ConfirmedAtUtc,
    bool IsManual);

public sealed record WmsScaleCaptureResult(
    WmsScaleCaptureDisposition Disposition,
    WmsStableWeight? Weight = null,
    string? ErrorCode = null,
    string? Message = null);

public sealed record WmsPrintRoute
{
    public required string Name { get; init; }

    public required string TemplateName { get; init; }

    public WmsPrintFormat Format { get; init; } = WmsPrintFormat.Pdf;

    public WmsPrintTransport Transport { get; init; } = WmsPrintTransport.BrowserPdf;

    public int? WarehouseId { get; init; }

    public string? StationCode { get; init; }

    public bool IsEnabled { get; init; } = true;

    public string? AdapterKey { get; init; }
}

public sealed record WmsPrintRequest(
    string TemplateName,
    WmsPrintFormat Format,
    int Copies = 1,
    int? WarehouseId = null,
    string? StationCode = null);

public sealed record WmsStationDeviceProfile
{
    public required string StationCode { get; init; }

    public int? WarehouseId { get; init; }

    public string? DefaultLocationCode { get; init; }

    public WmsScannerProfile? Scanner { get; init; }

    public WmsScaleProfile? Scale { get; init; }

    public WmsPrintRoute? LabelPrinter { get; init; }

    public WmsPrintRoute? DocumentPrinter { get; init; }

    public bool EnableSound { get; init; } = true;

    public bool EnableVibration { get; init; }

    public IReadOnlyDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(StationCode) || StationCode.Trim().Length > 100)
        {
            Add(errors, nameof(StationCode), "Station code is required and must be at most 100 characters.");
        }

        if (DefaultLocationCode?.Length > 100)
        {
            Add(errors, nameof(DefaultLocationCode), "Default location code must be at most 100 characters.");
        }

        AddNested(errors, nameof(Scanner), Scanner?.Validate());
        AddNested(errors, nameof(Scale), Scale?.Validate());
        ValidateRoute(errors, nameof(LabelPrinter), LabelPrinter);
        ValidateRoute(errors, nameof(DocumentPrinter), DocumentPrinter);
        return errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static void ValidateRoute(
        IDictionary<string, List<string>> errors,
        string prefix,
        WmsPrintRoute? route)
    {
        if (route is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(route.Name) || route.Name.Trim().Length > 100)
        {
            Add(errors, $"{prefix}.{nameof(WmsPrintRoute.Name)}", "Print route name is required and must be at most 100 characters.");
        }

        if (string.IsNullOrWhiteSpace(route.TemplateName) || route.TemplateName.Trim().Length > 100)
        {
            Add(errors, $"{prefix}.{nameof(WmsPrintRoute.TemplateName)}", "Print template name is required and must be at most 100 characters.");
        }

        if (route.Transport == WmsPrintTransport.BrowserPdf && route.Format != WmsPrintFormat.Pdf)
        {
            Add(errors, $"{prefix}.{nameof(WmsPrintRoute.Format)}", "Browser PDF routes must use PDF format.");
        }
    }

    private static void AddNested(
        IDictionary<string, List<string>> errors,
        string prefix,
        IReadOnlyDictionary<string, string[]>? nested)
    {
        if (nested is null)
        {
            return;
        }

        foreach (var pair in nested)
        {
            foreach (var message in pair.Value)
            {
                Add(errors, $"{prefix}.{pair.Key}", message);
            }
        }
    }

    private static void Add(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var values))
        {
            values = [];
            errors[key] = values;
        }

        values.Add(message);
    }
}

public interface IWmsScannerAdapter
{
    string AdapterKey { get; }

    WmsScanSource Source { get; }
}

public interface IWmsScaleAdapter
{
    string AdapterKey { get; }

    Task<WmsScaleReading?> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IWmsPrintAdapter
{
    string AdapterKey { get; }

    Task<Result> PrintAsync(
        WmsPrintRequest request,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
