using Wms.Application.Settings;

namespace Wms.Infrastructure.Settings;

public static class WmsSettingsSeedFactory
{
    public static WmsGlobalSettingsEntity Create(
        WmsSettingsValues values,
        int? defaultWarehouseId,
        DateTimeOffset now)
    {
        var entity = new WmsGlobalSettingsEntity
        {
            Id = WmsGlobalSettingsEntity.GlobalId,
            UpdatedAtUtc = now,
            Revision = 1
        };
        Apply(entity, values, defaultWarehouseId, now);
        return entity;
    }

    public static void Apply(
        WmsGlobalSettingsEntity entity,
        WmsSettingsValues values,
        int? defaultWarehouseId,
        DateTimeOffset now)
    {
        entity.CompanyName = values.Company.Name;
        entity.CompanyCode = values.Company.Code;
        entity.DefaultWarehouseId = defaultWarehouseId;
        entity.DefaultReceivingLocationCode = values.WarehouseDefaults.DefaultReceivingLocationCode;
        entity.DefaultShippingLocationCode = values.WarehouseDefaults.DefaultShippingLocationCode;
        entity.ReceivingPrefix = values.Numbering.ReceivingPrefix;
        entity.NextReceivingNumber = values.Numbering.NextReceivingNumber;
        entity.ShippingPrefix = values.Numbering.ShippingPrefix;
        entity.NextShippingNumber = values.Numbering.NextShippingNumber;
        entity.AdjustmentPrefix = values.Numbering.AdjustmentPrefix;
        entity.NextAdjustmentNumber = values.Numbering.NextAdjustmentNumber;
        entity.LowStockThreshold = values.Dashboard.LowStockThreshold;
        entity.LowStockAlertLimit = values.Dashboard.LowStockAlertLimit;
        entity.RecentMovementLimit = values.Dashboard.RecentMovementLimit;
        entity.DashboardRefreshIntervalSeconds = values.Dashboard.RefreshIntervalSeconds;
        entity.AllowNegativeStock = values.Inventory.AllowNegativeStock;
        entity.RequireLocationForAdjustment = values.Inventory.RequireLocationForAdjustment;
        entity.MaximumAdjustmentQuantity = values.Inventory.MaximumAdjustmentQuantity;
        entity.ExpiryWarningDays = values.Expiry.WarningDays;
        entity.BlockExpiredReceipt = values.Expiry.BlockExpiredReceipt;
        entity.ScannerTimeoutMilliseconds = values.Scanner.TimeoutMilliseconds;
        entity.MinimumBarcodeLength = values.Scanner.MinimumBarcodeLength;
        entity.MaximumBarcodeLength = values.Scanner.MaximumBarcodeLength;
        entity.EnableAudioFeedback = values.Scanner.EnableAudioFeedback;
        entity.LabelTemplateName = values.Labels.TemplateName;
        entity.LabelPaperSize = values.Labels.PaperSize;
        entity.IncludeCompanyNameOnLabels = values.Labels.IncludeCompanyName;
        entity.DefaultReportPeriodDays = values.Reports.DefaultPeriodDays;
        entity.MaximumReportRows = values.Reports.MaximumRows;
        entity.DefaultLocale = values.Localization.Locale;
        entity.DefaultTimeZone = values.Localization.TimeZone;
        entity.CurrencyCode = values.Localization.CurrencyCode;
        entity.IntegrationsEnabled = values.Integrations.Enabled;
        entity.IntegrationEndpointUrl = values.Integrations.EndpointUrl;
        entity.IntegrationTimeoutSeconds = values.Integrations.TimeoutSeconds;
        entity.UpdatedAtUtc = now;
    }
}
