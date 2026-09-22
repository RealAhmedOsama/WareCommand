using System.ComponentModel.DataAnnotations;
using Wms.Application.Identity;
using Wms.Application.Settings;

namespace Wms.ASP.Models;

public sealed class SettingsIndexViewModel
{
    public int? WarehouseId { get; set; }
    public string ScopeName { get; set; } = "Global settings";
    public GlobalSettingsFormViewModel Global { get; set; } = new();
    public WarehouseSettingsOverrideFormViewModel Override { get; set; } = new();
    public WmsSettingsValues EffectiveValues { get; set; } = WmsSettingsDefaults.Create();
    public IReadOnlyList<WmsWarehouseOption> Warehouses { get; set; } = [];
}

public sealed class GlobalSettingsFormViewModel
{
    [Required, StringLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    public string CompanyCode { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int? DefaultWarehouseId { get; set; }

    [Required, StringLength(50)]
    public string DefaultReceivingLocationCode { get; set; } = string.Empty;

    [StringLength(50)]
    public string? DefaultShippingLocationCode { get; set; }

    [Required, StringLength(20)]
    public string ReceivingPrefix { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long NextReceivingNumber { get; set; } = 1;

    [Required, StringLength(20)]
    public string ShippingPrefix { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long NextShippingNumber { get; set; } = 1;

    [Required, StringLength(20)]
    public string AdjustmentPrefix { get; set; } = string.Empty;

    [Range(1, long.MaxValue)]
    public long NextAdjustmentNumber { get; set; } = 1;

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal LowStockThreshold { get; set; }

    [Range(1, 1000)]
    public int LowStockAlertLimit { get; set; } = 10;

    [Range(1, 1000)]
    public int RecentMovementLimit { get; set; } = 10;

    [Range(5, 86400)]
    public int RefreshIntervalSeconds { get; set; } = 300;

    public bool AllowNegativeStock { get; set; }
    public bool RequireLocationForAdjustment { get; set; } = true;

    [Wms.ASP.Validation.InvariantDecimalRange("0.0001", "1000000000")]
    public decimal MaximumAdjustmentQuantity { get; set; } = 1_000_000m;

    [Range(0, 3650)]
    public int ExpiryWarningDays { get; set; } = 30;

    public bool BlockExpiredReceipt { get; set; } = true;

    [Range(100, 600000)]
    public int ScannerTimeoutMilliseconds { get; set; } = 5000;

    [Range(1, 100)]
    public int MinimumBarcodeLength { get; set; } = 3;

    [Range(1, 500)]
    public int MaximumBarcodeLength { get; set; } = 50;

    public bool EnableAudioFeedback { get; set; } = true;

    [Required, StringLength(100)]
    public string LabelTemplateName { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string LabelPaperSize { get; set; } = string.Empty;

    public bool IncludeCompanyNameOnLabels { get; set; } = true;

    [Range(1, 3650)]
    public int DefaultReportPeriodDays { get; set; } = 7;

    [Range(1, 100000)]
    public int MaximumReportRows { get; set; } = 10000;

    [Required, StringLength(20)]
    public string Locale { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string TimeZone { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string CurrencyCode { get; set; } = string.Empty;

    public bool IntegrationsEnabled { get; set; }

    [StringLength(2000)]
    public string? IntegrationEndpointUrl { get; set; }

    [Range(1, 300)]
    public int IntegrationTimeoutSeconds { get; set; } = 30;

    public static GlobalSettingsFormViewModel From(WmsSettingsValues values) => new()
    {
        CompanyName = values.Company.Name,
        CompanyCode = values.Company.Code,
        DefaultWarehouseId = values.WarehouseDefaults.DefaultWarehouseId,
        DefaultReceivingLocationCode = values.WarehouseDefaults.DefaultReceivingLocationCode,
        DefaultShippingLocationCode = values.WarehouseDefaults.DefaultShippingLocationCode,
        ReceivingPrefix = values.Numbering.ReceivingPrefix,
        NextReceivingNumber = values.Numbering.NextReceivingNumber,
        ShippingPrefix = values.Numbering.ShippingPrefix,
        NextShippingNumber = values.Numbering.NextShippingNumber,
        AdjustmentPrefix = values.Numbering.AdjustmentPrefix,
        NextAdjustmentNumber = values.Numbering.NextAdjustmentNumber,
        LowStockThreshold = values.Dashboard.LowStockThreshold,
        LowStockAlertLimit = values.Dashboard.LowStockAlertLimit,
        RecentMovementLimit = values.Dashboard.RecentMovementLimit,
        RefreshIntervalSeconds = values.Dashboard.RefreshIntervalSeconds,
        AllowNegativeStock = values.Inventory.AllowNegativeStock,
        RequireLocationForAdjustment = values.Inventory.RequireLocationForAdjustment,
        MaximumAdjustmentQuantity = values.Inventory.MaximumAdjustmentQuantity,
        ExpiryWarningDays = values.Expiry.WarningDays,
        BlockExpiredReceipt = values.Expiry.BlockExpiredReceipt,
        ScannerTimeoutMilliseconds = values.Scanner.TimeoutMilliseconds,
        MinimumBarcodeLength = values.Scanner.MinimumBarcodeLength,
        MaximumBarcodeLength = values.Scanner.MaximumBarcodeLength,
        EnableAudioFeedback = values.Scanner.EnableAudioFeedback,
        LabelTemplateName = values.Labels.TemplateName,
        LabelPaperSize = values.Labels.PaperSize,
        IncludeCompanyNameOnLabels = values.Labels.IncludeCompanyName,
        DefaultReportPeriodDays = values.Reports.DefaultPeriodDays,
        MaximumReportRows = values.Reports.MaximumRows,
        Locale = values.Localization.Locale,
        TimeZone = values.Localization.TimeZone,
        CurrencyCode = values.Localization.CurrencyCode,
        IntegrationsEnabled = values.Integrations.Enabled,
        IntegrationEndpointUrl = values.Integrations.EndpointUrl,
        IntegrationTimeoutSeconds = values.Integrations.TimeoutSeconds
    };

    public WmsSettingsValues ToValues() => new()
    {
        Company = new WmsCompanySettings { Name = CompanyName, Code = CompanyCode },
        WarehouseDefaults = new WmsWarehouseDefaultsSettings
        {
            DefaultWarehouseId = DefaultWarehouseId,
            DefaultReceivingLocationCode = DefaultReceivingLocationCode,
            DefaultShippingLocationCode = DefaultShippingLocationCode
        },
        Numbering = new WmsNumberingSettings
        {
            ReceivingPrefix = ReceivingPrefix,
            NextReceivingNumber = NextReceivingNumber,
            ShippingPrefix = ShippingPrefix,
            NextShippingNumber = NextShippingNumber,
            AdjustmentPrefix = AdjustmentPrefix,
            NextAdjustmentNumber = NextAdjustmentNumber
        },
        Dashboard = new WmsDashboardSettings
        {
            LowStockThreshold = LowStockThreshold,
            LowStockAlertLimit = LowStockAlertLimit,
            RecentMovementLimit = RecentMovementLimit,
            RefreshIntervalSeconds = RefreshIntervalSeconds
        },
        Inventory = new WmsInventorySettings
        {
            AllowNegativeStock = AllowNegativeStock,
            RequireLocationForAdjustment = RequireLocationForAdjustment,
            MaximumAdjustmentQuantity = MaximumAdjustmentQuantity
        },
        Expiry = new WmsExpirySettings
        {
            WarningDays = ExpiryWarningDays,
            BlockExpiredReceipt = BlockExpiredReceipt
        },
        Scanner = new WmsScannerSettings
        {
            TimeoutMilliseconds = ScannerTimeoutMilliseconds,
            MinimumBarcodeLength = MinimumBarcodeLength,
            MaximumBarcodeLength = MaximumBarcodeLength,
            EnableAudioFeedback = EnableAudioFeedback
        },
        Labels = new WmsLabelSettings
        {
            TemplateName = LabelTemplateName,
            PaperSize = LabelPaperSize,
            IncludeCompanyName = IncludeCompanyNameOnLabels
        },
        Reports = new WmsReportSettings
        {
            DefaultPeriodDays = DefaultReportPeriodDays,
            MaximumRows = MaximumReportRows
        },
        Localization = new WmsLocalizationSettings
        {
            Locale = Locale,
            TimeZone = TimeZone,
            CurrencyCode = CurrencyCode
        },
        Integrations = new WmsIntegrationSettings
        {
            Enabled = IntegrationsEnabled,
            EndpointUrl = IntegrationEndpointUrl,
            TimeoutSeconds = IntegrationTimeoutSeconds
        }
    };
}

public sealed class WarehouseSettingsOverrideFormViewModel
{
    [Required, Range(1, int.MaxValue)]
    public int WarehouseId { get; set; }

    [StringLength(50)]
    public string? DefaultReceivingLocationCode { get; set; }

    [StringLength(50)]
    public string? DefaultShippingLocationCode { get; set; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal? LowStockThreshold { get; set; }

    [Range(1, 1000)]
    public int? LowStockAlertLimit { get; set; }

    [Range(1, 1000)]
    public int? RecentMovementLimit { get; set; }

    [Range(5, 86400)]
    public int? RefreshIntervalSeconds { get; set; }

    [Range(0, 3650)]
    public int? ExpiryWarningDays { get; set; }

    public bool? BlockExpiredReceipt { get; set; }

    [Range(100, 600000)]
    public int? ScannerTimeoutMilliseconds { get; set; }

    [Range(1, 100)]
    public int? MinimumBarcodeLength { get; set; }

    [Range(1, 500)]
    public int? MaximumBarcodeLength { get; set; }

    public bool? EnableAudioFeedback { get; set; }

    [Range(1, 3650)]
    public int? DefaultReportPeriodDays { get; set; }

    [Range(1, 100000)]
    public int? MaximumReportRows { get; set; }

    [StringLength(20)]
    public string? Locale { get; set; }

    [StringLength(100)]
    public string? TimeZone { get; set; }

    public static WarehouseSettingsOverrideFormViewModel From(
        int warehouseId,
        WmsWarehouseSettingsOverrides values) => new()
        {
            WarehouseId = warehouseId,
            DefaultReceivingLocationCode = values.DefaultReceivingLocationCode,
            DefaultShippingLocationCode = values.DefaultShippingLocationCode,
            LowStockThreshold = values.LowStockThreshold,
            LowStockAlertLimit = values.LowStockAlertLimit,
            RecentMovementLimit = values.RecentMovementLimit,
            RefreshIntervalSeconds = values.RefreshIntervalSeconds,
            ExpiryWarningDays = values.ExpiryWarningDays,
            BlockExpiredReceipt = values.BlockExpiredReceipt,
            ScannerTimeoutMilliseconds = values.ScannerTimeoutMilliseconds,
            MinimumBarcodeLength = values.MinimumBarcodeLength,
            MaximumBarcodeLength = values.MaximumBarcodeLength,
            EnableAudioFeedback = values.EnableAudioFeedback,
            DefaultReportPeriodDays = values.DefaultReportPeriodDays,
            MaximumReportRows = values.MaximumReportRows,
            Locale = values.Locale,
            TimeZone = values.TimeZone
        };

    public WmsWarehouseSettingsOverrides ToValues() => new()
    {
        DefaultReceivingLocationCode = NullIfWhiteSpace(DefaultReceivingLocationCode),
        DefaultShippingLocationCode = NullIfWhiteSpace(DefaultShippingLocationCode),
        LowStockThreshold = LowStockThreshold,
        LowStockAlertLimit = LowStockAlertLimit,
        RecentMovementLimit = RecentMovementLimit,
        RefreshIntervalSeconds = RefreshIntervalSeconds,
        ExpiryWarningDays = ExpiryWarningDays,
        BlockExpiredReceipt = BlockExpiredReceipt,
        ScannerTimeoutMilliseconds = ScannerTimeoutMilliseconds,
        MinimumBarcodeLength = MinimumBarcodeLength,
        MaximumBarcodeLength = MaximumBarcodeLength,
        EnableAudioFeedback = EnableAudioFeedback,
        DefaultReportPeriodDays = DefaultReportPeriodDays,
        MaximumReportRows = MaximumReportRows,
        Locale = NullIfWhiteSpace(Locale),
        TimeZone = NullIfWhiteSpace(TimeZone)
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class SettingsImportViewModel
{
    [Required]
    [StringLength(500_000)]
    public string ImportJson { get; set; } = string.Empty;
}
