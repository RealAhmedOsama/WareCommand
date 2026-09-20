namespace Wms.Infrastructure.Settings;

/// <summary>
/// One explicit global settings row. The fixed row key prevents multiple global
/// profiles while the named columns keep the persisted schema auditable.
/// </summary>
public sealed class WmsGlobalSettingsEntity
{
    public const int GlobalId = 1;

    public int Id { get; set; } = GlobalId;
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyCode { get; set; } = string.Empty;
    public int? DefaultWarehouseId { get; set; }
    public string DefaultReceivingLocationCode { get; set; } = string.Empty;
    public string? DefaultShippingLocationCode { get; set; }
    public string ReceivingPrefix { get; set; } = string.Empty;
    public long NextReceivingNumber { get; set; }
    public string ShippingPrefix { get; set; } = string.Empty;
    public long NextShippingNumber { get; set; }
    public string AdjustmentPrefix { get; set; } = string.Empty;
    public long NextAdjustmentNumber { get; set; }
    public decimal LowStockThreshold { get; set; }
    public int LowStockAlertLimit { get; set; }
    public int RecentMovementLimit { get; set; }
    public int DashboardRefreshIntervalSeconds { get; set; }
    public bool AllowNegativeStock { get; set; }
    public bool RequireLocationForAdjustment { get; set; }
    public decimal MaximumAdjustmentQuantity { get; set; }
    public int ExpiryWarningDays { get; set; }
    public bool BlockExpiredReceipt { get; set; }
    public int ScannerTimeoutMilliseconds { get; set; }
    public int MinimumBarcodeLength { get; set; }
    public int MaximumBarcodeLength { get; set; }
    public bool EnableAudioFeedback { get; set; }
    public string LabelTemplateName { get; set; } = string.Empty;
    public string LabelPaperSize { get; set; } = string.Empty;
    public bool IncludeCompanyNameOnLabels { get; set; }
    public int DefaultReportPeriodDays { get; set; }
    public int MaximumReportRows { get; set; }
    public string DefaultLocale { get; set; } = string.Empty;
    public string DefaultTimeZone { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public bool IntegrationsEnabled { get; set; }
    public string? IntegrationEndpointUrl { get; set; }
    public int IntegrationTimeoutSeconds { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Revision { get; set; } = 1;
}

/// <summary>
/// Nullable fields are deliberate: a warehouse only stores settings it
/// overrides and inherits every other value from the global row.
/// </summary>
public sealed class WmsWarehouseSettingsOverrideEntity
{
    public int WarehouseId { get; set; }
    public string? DefaultReceivingLocationCode { get; set; }
    public string? DefaultShippingLocationCode { get; set; }
    public decimal? LowStockThreshold { get; set; }
    public int? LowStockAlertLimit { get; set; }
    public int? RecentMovementLimit { get; set; }
    public int? DashboardRefreshIntervalSeconds { get; set; }
    public int? ExpiryWarningDays { get; set; }
    public bool? BlockExpiredReceipt { get; set; }
    public int? ScannerTimeoutMilliseconds { get; set; }
    public int? MinimumBarcodeLength { get; set; }
    public int? MaximumBarcodeLength { get; set; }
    public bool? EnableAudioFeedback { get; set; }
    public int? DefaultReportPeriodDays { get; set; }
    public int? MaximumReportRows { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public long Revision { get; set; } = 1;
}
