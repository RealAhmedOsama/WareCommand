using System.Globalization;

namespace Wms.Application.Settings;

/// <summary>
/// The business settings schema. This is intentionally explicit: adding a new
/// operational rule requires adding a named property and its validation rather
/// than creating an unbounded key/value store.
/// </summary>
public sealed class WmsSettingsValues
{
    public WmsCompanySettings Company { get; init; } = new();
    public WmsWarehouseDefaultsSettings WarehouseDefaults { get; init; } = new();
    public WmsNumberingSettings Numbering { get; init; } = new();
    public WmsDashboardSettings Dashboard { get; init; } = new();
    public WmsInventorySettings Inventory { get; init; } = new();
    public WmsExpirySettings Expiry { get; init; } = new();
    public WmsScannerSettings Scanner { get; init; } = new();
    public WmsLabelSettings Labels { get; init; } = new();
    public WmsReportSettings Reports { get; init; } = new();
    public WmsLocalizationSettings Localization { get; init; } = new();
    public WmsIntegrationSettings Integrations { get; init; } = new();
}

public sealed class WmsCompanySettings
{
    public string Name { get; init; } = "WareCommand";
    public string Code { get; init; } = "WARECOMMAND";
}

public sealed class WmsWarehouseDefaultsSettings
{
    public int? DefaultWarehouseId { get; init; }
    public string DefaultReceivingLocationCode { get; init; } = "RECEIVE";
    public string? DefaultShippingLocationCode { get; init; }
}

public sealed class WmsNumberingSettings
{
    public string ReceivingPrefix { get; init; } = "RCV-";
    public long NextReceivingNumber { get; init; } = 1;
    public string ShippingPrefix { get; init; } = "SHP-";
    public long NextShippingNumber { get; init; } = 1;
    public string AdjustmentPrefix { get; init; } = "ADJ-";
    public long NextAdjustmentNumber { get; init; } = 1;
}

public sealed class WmsDashboardSettings
{
    public decimal LowStockThreshold { get; init; } = 10m;
    public int LowStockAlertLimit { get; init; } = 10;
    public int RecentMovementLimit { get; init; } = 10;
    public int RefreshIntervalSeconds { get; init; } = 300;
}

public sealed class WmsInventorySettings
{
    public bool AllowNegativeStock { get; init; }
    public bool RequireLocationForAdjustment { get; init; } = true;
    public decimal MaximumAdjustmentQuantity { get; init; } = 1_000_000m;
}

public sealed class WmsExpirySettings
{
    public int WarningDays { get; init; } = 30;
    public bool BlockExpiredReceipt { get; init; } = true;
}

public sealed class WmsScannerSettings
{
    public int TimeoutMilliseconds { get; init; } = 5_000;
    public int MinimumBarcodeLength { get; init; } = 3;
    public int MaximumBarcodeLength { get; init; } = 50;
    public bool EnableAudioFeedback { get; init; } = true;
}

public sealed class WmsLabelSettings
{
    public string TemplateName { get; init; } = "default";
    public string PaperSize { get; init; } = "A4";
    public bool IncludeCompanyName { get; init; } = true;
}

public sealed class WmsReportSettings
{
    public int DefaultPeriodDays { get; init; } = 7;
    public int MaximumRows { get; init; } = 10_000;
}

public sealed class WmsLocalizationSettings
{
    public string Locale { get; init; } = "en-US";
    public string TimeZone { get; init; } = "UTC";
    public string CurrencyCode { get; init; } = "USD";
}

/// <summary>
/// Integrations contain only non-secret routing and behavior. Credentials are
/// deployment concerns and must be supplied by the host secret provider.
/// </summary>
public sealed class WmsIntegrationSettings
{
    public bool Enabled { get; init; }
    public string? EndpointUrl { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Nullable values represent an intentional warehouse override. Null means
/// inherit the corresponding global value.
/// </summary>
public sealed class WmsWarehouseSettingsOverrides
{
    public string? DefaultReceivingLocationCode { get; init; }
    public string? DefaultShippingLocationCode { get; init; }
    public decimal? LowStockThreshold { get; init; }
    public int? LowStockAlertLimit { get; init; }
    public int? RecentMovementLimit { get; init; }
    public int? RefreshIntervalSeconds { get; init; }
    public int? ExpiryWarningDays { get; init; }
    public bool? BlockExpiredReceipt { get; init; }
    public int? ScannerTimeoutMilliseconds { get; init; }
    public int? MinimumBarcodeLength { get; init; }
    public int? MaximumBarcodeLength { get; init; }
    public bool? EnableAudioFeedback { get; init; }
    public int? DefaultReportPeriodDays { get; init; }
    public int? MaximumReportRows { get; init; }
    public string? Locale { get; init; }
    public string? TimeZone { get; init; }
}

public sealed record WmsSettingsSnapshot(
    int? WarehouseId,
    string Scope,
    DateTimeOffset UpdatedAtUtc,
    WmsSettingsValues Values,
    WmsWarehouseSettingsOverrides? Overrides = null);

public sealed class WmsSettingsExportDocument
{
    public int SchemaVersion { get; init; } = 1;
    public WmsSettingsValues Global { get; init; } = new();
    public IReadOnlyList<WmsWarehouseSettingsExport> WarehouseOverrides { get; init; } = [];
}

public sealed class WmsWarehouseSettingsExport
{
    public int WarehouseId { get; init; }
    public WmsWarehouseSettingsOverrides Values { get; init; } = new();
}

public static class WmsSettingsDefaults
{
    public static WmsSettingsValues Create() => new();
}

public static class WmsSettingsPrecedence
{
    public static WmsSettingsValues Apply(
        WmsSettingsValues global,
        WmsWarehouseSettingsOverrides? overrides)
    {
        ArgumentNullException.ThrowIfNull(global);
        if (overrides is null)
        {
            return global;
        }

        return new WmsSettingsValues
        {
            Company = global.Company,
            WarehouseDefaults = new WmsWarehouseDefaultsSettings
            {
                DefaultWarehouseId = global.WarehouseDefaults.DefaultWarehouseId,
                DefaultReceivingLocationCode = First(overrides.DefaultReceivingLocationCode,
                    global.WarehouseDefaults.DefaultReceivingLocationCode) ?? string.Empty,
                DefaultShippingLocationCode = First(overrides.DefaultShippingLocationCode,
                    global.WarehouseDefaults.DefaultShippingLocationCode)
            },
            Numbering = global.Numbering,
            Dashboard = new WmsDashboardSettings
            {
                LowStockThreshold = overrides.LowStockThreshold ?? global.Dashboard.LowStockThreshold,
                LowStockAlertLimit = overrides.LowStockAlertLimit ?? global.Dashboard.LowStockAlertLimit,
                RecentMovementLimit = overrides.RecentMovementLimit ?? global.Dashboard.RecentMovementLimit,
                RefreshIntervalSeconds = overrides.RefreshIntervalSeconds ?? global.Dashboard.RefreshIntervalSeconds
            },
            Inventory = global.Inventory,
            Expiry = new WmsExpirySettings
            {
                WarningDays = overrides.ExpiryWarningDays ?? global.Expiry.WarningDays,
                BlockExpiredReceipt = overrides.BlockExpiredReceipt ?? global.Expiry.BlockExpiredReceipt
            },
            Scanner = new WmsScannerSettings
            {
                TimeoutMilliseconds = overrides.ScannerTimeoutMilliseconds ?? global.Scanner.TimeoutMilliseconds,
                MinimumBarcodeLength = overrides.MinimumBarcodeLength ?? global.Scanner.MinimumBarcodeLength,
                MaximumBarcodeLength = overrides.MaximumBarcodeLength ?? global.Scanner.MaximumBarcodeLength,
                EnableAudioFeedback = overrides.EnableAudioFeedback ?? global.Scanner.EnableAudioFeedback
            },
            Labels = global.Labels,
            Reports = new WmsReportSettings
            {
                DefaultPeriodDays = overrides.DefaultReportPeriodDays ?? global.Reports.DefaultPeriodDays,
                MaximumRows = overrides.MaximumReportRows ?? global.Reports.MaximumRows
            },
            Localization = new WmsLocalizationSettings
            {
                Locale = First(overrides.Locale, global.Localization.Locale)!,
                TimeZone = First(overrides.TimeZone, global.Localization.TimeZone)!,
                CurrencyCode = global.Localization.CurrencyCode
            },
            Integrations = global.Integrations
        };
    }

    private static string? First(string? value, string? fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public static class WmsSettingsValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate(WmsSettingsValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        RequireLength(errors, "Company.Name", values.Company.Name, 1, 200);
        RequirePattern(errors, "Company.Code", values.Company.Code, 2, 50);
        RequireLength(errors, "WarehouseDefaults.DefaultReceivingLocationCode",
            values.WarehouseDefaults.DefaultReceivingLocationCode, 1, 50);
        OptionalLength(errors, "WarehouseDefaults.DefaultShippingLocationCode",
            values.WarehouseDefaults.DefaultShippingLocationCode, 50);

        RequirePrefix(errors, "Numbering.ReceivingPrefix", values.Numbering.ReceivingPrefix);
        RequirePositive(errors, "Numbering.NextReceivingNumber", values.Numbering.NextReceivingNumber);
        RequirePrefix(errors, "Numbering.ShippingPrefix", values.Numbering.ShippingPrefix);
        RequirePositive(errors, "Numbering.NextShippingNumber", values.Numbering.NextShippingNumber);
        RequirePrefix(errors, "Numbering.AdjustmentPrefix", values.Numbering.AdjustmentPrefix);
        RequirePositive(errors, "Numbering.NextAdjustmentNumber", values.Numbering.NextAdjustmentNumber);

        if (values.Dashboard.LowStockThreshold < 0 || values.Dashboard.LowStockThreshold > 1_000_000_000m)
        {
            Add(errors, "Dashboard.LowStockThreshold", "Low-stock threshold must be between 0 and 1,000,000,000.");
        }

        if (values.Dashboard.LowStockAlertLimit is < 1 or > 1_000)
        {
            Add(errors, "Dashboard.LowStockAlertLimit", "Low-stock alert limit must be between 1 and 1,000.");
        }

        if (values.Dashboard.RecentMovementLimit is < 1 or > 1_000)
        {
            Add(errors, "Dashboard.RecentMovementLimit", "Recent movement limit must be between 1 and 1,000.");
        }

        if (values.Dashboard.RefreshIntervalSeconds is < 5 or > 86_400)
        {
            Add(errors, "Dashboard.RefreshIntervalSeconds", "Dashboard refresh interval must be between 5 seconds and 24 hours.");
        }

        if (values.Inventory.MaximumAdjustmentQuantity <= 0 ||
            values.Inventory.MaximumAdjustmentQuantity > 1_000_000_000m)
        {
            Add(errors, "Inventory.MaximumAdjustmentQuantity", "Maximum adjustment quantity must be positive and bounded.");
        }

        if (values.Expiry.WarningDays is < 0 or > 3_650)
        {
            Add(errors, "Expiry.WarningDays", "Expiry warning days must be between 0 and 3,650.");
        }

        ValidateScanner(errors, values.Scanner);
        RequireLength(errors, "Labels.TemplateName", values.Labels.TemplateName, 1, 100);
        RequireLength(errors, "Labels.PaperSize", values.Labels.PaperSize, 1, 30);

        if (values.Reports.DefaultPeriodDays is < 1 or > 3_650)
        {
            Add(errors, "Reports.DefaultPeriodDays", "Default report period must be between 1 and 3,650 days.");
        }

        if (values.Reports.MaximumRows is < 1 or > 100_000)
        {
            Add(errors, "Reports.MaximumRows", "Maximum report rows must be between 1 and 100,000.");
        }

        ValidateLocalization(errors, values.Localization);
        ValidateIntegrations(errors, values.Integrations);

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, string[]> ValidateOverrides(
        WmsWarehouseSettingsOverrides values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        OptionalLength(errors, "DefaultReceivingLocationCode", values.DefaultReceivingLocationCode, 50);
        OptionalLength(errors, "DefaultShippingLocationCode", values.DefaultShippingLocationCode, 50);

        if (values.LowStockThreshold is < 0 or > 1_000_000_000m)
        {
            Add(errors, "LowStockThreshold", "Low-stock threshold must be between 0 and 1,000,000,000.");
        }

        if (values.LowStockAlertLimit is < 1 or > 1_000)
        {
            Add(errors, "LowStockAlertLimit", "Low-stock alert limit must be between 1 and 1,000.");
        }

        if (values.RecentMovementLimit is < 1 or > 1_000)
        {
            Add(errors, "RecentMovementLimit", "Recent movement limit must be between 1 and 1,000.");
        }

        if (values.RefreshIntervalSeconds is < 5 or > 86_400)
        {
            Add(errors, "RefreshIntervalSeconds", "Dashboard refresh interval must be between 5 seconds and 24 hours.");
        }

        if (values.ExpiryWarningDays is < 0 or > 3_650)
        {
            Add(errors, "ExpiryWarningDays", "Expiry warning days must be between 0 and 3,650.");
        }

        if (values.ScannerTimeoutMilliseconds is < 100 or > 600_000)
        {
            Add(errors, "ScannerTimeoutMilliseconds", "Scanner timeout must be between 100 and 600,000 milliseconds.");
        }

        if (values.MinimumBarcodeLength is < 1 or > 100)
        {
            Add(errors, "MinimumBarcodeLength", "Minimum barcode length must be between 1 and 100.");
        }

        if (values.MaximumBarcodeLength is < 1 or > 500)
        {
            Add(errors, "MaximumBarcodeLength", "Maximum barcode length must be between 1 and 500.");
        }

        if (values.MinimumBarcodeLength.HasValue && values.MaximumBarcodeLength.HasValue &&
            values.MinimumBarcodeLength > values.MaximumBarcodeLength)
        {
            Add(errors, "MaximumBarcodeLength", "Maximum barcode length cannot be less than the minimum.");
        }

        if (values.DefaultReportPeriodDays is < 1 or > 3_650)
        {
            Add(errors, "DefaultReportPeriodDays", "Default report period must be between 1 and 3,650 days.");
        }

        if (values.MaximumReportRows is < 1 or > 100_000)
        {
            Add(errors, "MaximumReportRows", "Maximum report rows must be between 1 and 100,000.");
        }

        OptionalLength(errors, "Locale", values.Locale, 20);
        OptionalLength(errors, "TimeZone", values.TimeZone, 100);

        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static void ValidateScanner(
        IDictionary<string, List<string>> errors,
        WmsScannerSettings values)
    {
        if (values.TimeoutMilliseconds is < 100 or > 600_000)
        {
            Add(errors, "Scanner.TimeoutMilliseconds", "Scanner timeout must be between 100 and 600,000 milliseconds.");
        }

        if (values.MinimumBarcodeLength is < 1 or > 100)
        {
            Add(errors, "Scanner.MinimumBarcodeLength", "Minimum barcode length must be between 1 and 100.");
        }

        if (values.MaximumBarcodeLength is < 1 or > 500)
        {
            Add(errors, "Scanner.MaximumBarcodeLength", "Maximum barcode length must be between 1 and 500.");
        }

        if (values.MinimumBarcodeLength > values.MaximumBarcodeLength)
        {
            Add(errors, "Scanner.MaximumBarcodeLength", "Maximum barcode length cannot be less than the minimum.");
        }
    }

    private static void ValidateLocalization(
        IDictionary<string, List<string>> errors,
        WmsLocalizationSettings values)
    {
        RequireLength(errors, "Localization.Locale", values.Locale, 2, 20);
        RequireLength(errors, "Localization.TimeZone", values.TimeZone, 1, 100);
        RequirePattern(errors, "Localization.CurrencyCode", values.CurrencyCode, 3, 3);

        try
        {
            _ = CultureInfo.GetCultureInfo(values.Locale);
        }
        catch (CultureNotFoundException)
        {
            Add(errors, "Localization.Locale", "Locale must be a recognized culture name.");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(values.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            Add(errors, "Localization.TimeZone", "Time zone must be installed on the host.");
        }
        catch (InvalidTimeZoneException)
        {
            Add(errors, "Localization.TimeZone", "Time zone is invalid on the host.");
        }
    }

    private static void ValidateIntegrations(
        IDictionary<string, List<string>> errors,
        WmsIntegrationSettings values)
    {
        if (values.TimeoutSeconds is < 1 or > 300)
        {
            Add(errors, "Integrations.TimeoutSeconds", "Integration timeout must be between 1 and 300 seconds.");
        }

        var hasValidEndpoint = Uri.TryCreate(values.EndpointUrl, UriKind.Absolute, out var endpoint) &&
                               endpoint is { Scheme: "http" or "https" };
        if (values.Enabled && !hasValidEndpoint)
        {
            Add(errors, "Integrations.EndpointUrl", "An enabled integration requires an absolute HTTP or HTTPS endpoint.");
        }
        else if (!string.IsNullOrWhiteSpace(values.EndpointUrl) &&
                 (!Uri.TryCreate(values.EndpointUrl, UriKind.Absolute, out var optionalEndpoint) ||
                  optionalEndpoint is not { Scheme: "http" or "https" }))
        {
            Add(errors, "Integrations.EndpointUrl", "Integration endpoint must be an absolute HTTP or HTTPS URL.");
        }
    }

    private static void RequirePrefix(
        IDictionary<string, List<string>> errors,
        string key,
        string value)
    {
        RequireLength(errors, key, value, 1, 20);
        if (!string.IsNullOrWhiteSpace(value) && value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '/'))
        {
            Add(errors, key, "Numbering prefixes may contain only letters, digits, '-', '_', and '/'.");
        }
    }

    private static void RequirePositive(
        IDictionary<string, List<string>> errors,
        string key,
        long value)
    {
        if (value < 1)
        {
            Add(errors, key, "The sequence must start at a positive number.");
        }
    }

    private static void RequirePattern(
        IDictionary<string, List<string>> errors,
        string key,
        string value,
        int minimumLength,
        int maximumLength)
    {
        RequireLength(errors, key, value, minimumLength, maximumLength);
        if (!string.IsNullOrWhiteSpace(value) && value.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            Add(errors, key, "The value may contain only letters, digits, '-', and '_'.");
        }
    }

    private static void RequireLength(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int minimumLength,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < minimumLength || value.Trim().Length > maximumLength)
        {
            Add(errors, key, $"The value is required and must be between {minimumLength} and {maximumLength} characters.");
        }
    }

    private static void OptionalLength(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int maximumLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maximumLength)
        {
            Add(errors, key, $"The value cannot exceed {maximumLength} characters.");
        }
    }

    private static void Add(
        IDictionary<string, List<string>> errors,
        string key,
        string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}

public interface IWmsSettingsService
{
    Task<Common.Result<WmsSettingsSnapshot>> GetAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<Common.Result<WmsSettingsExportDocument>> ExportAsync(
        CancellationToken cancellationToken = default);

    Task<Common.Result<WmsSettingsSnapshot>> SaveGlobalAsync(
        WmsSettingsValues values,
        CancellationToken cancellationToken = default);

    Task<Common.Result<WmsSettingsSnapshot>> SaveWarehouseOverrideAsync(
        int warehouseId,
        WmsWarehouseSettingsOverrides values,
        CancellationToken cancellationToken = default);

    Task<Common.Result<WmsSettingsSnapshot>> ImportAsync(
        WmsSettingsExportDocument document,
        CancellationToken cancellationToken = default);
}
