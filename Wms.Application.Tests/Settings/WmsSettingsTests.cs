using Wms.Application.Settings;

namespace Wms.Application.Tests.Settings;

public sealed class WmsSettingsTests
{
    [Fact]
    public void WarehouseOverridesWinOnlyForTheFieldsTheySpecify()
    {
        var global = WmsSettingsDefaults.Create();
        var overrides = new WmsWarehouseSettingsOverrides
        {
            LowStockThreshold = 25m,
            RefreshIntervalSeconds = 60,
            DefaultReceivingLocationCode = "DOCK-2"
        };

        var effective = WmsSettingsPrecedence.Apply(global, overrides);

        effective.Dashboard.LowStockThreshold.Should().Be(25m);
        effective.Dashboard.RefreshIntervalSeconds.Should().Be(60);
        effective.WarehouseDefaults.DefaultReceivingLocationCode.Should().Be("DOCK-2");
        effective.Dashboard.LowStockAlertLimit.Should().Be(global.Dashboard.LowStockAlertLimit);
        effective.Scanner.MaximumBarcodeLength.Should().Be(global.Scanner.MaximumBarcodeLength);
        effective.Numbering.ReceivingPrefix.Should().Be(global.Numbering.ReceivingPrefix);
    }

    [Fact]
    public void DefaultsAreStronglyTypedAndValid()
    {
        var errors = WmsSettingsValidation.Validate(WmsSettingsDefaults.Create());

        errors.Should().BeEmpty();
    }

    [Fact]
    public void InvalidScannerRangeIsRejectedBeforePersistence()
    {
        var values = new WmsSettingsValues
        {
            Scanner = new WmsScannerSettings
            {
                MinimumBarcodeLength = 50,
                MaximumBarcodeLength = 10
            }
        };

        var errors = WmsSettingsValidation.Validate(values);

        errors.Should().ContainKey("Scanner.MaximumBarcodeLength");
        errors["Scanner.MaximumBarcodeLength"]
            .Should().Contain(error => error.Contains("cannot be less", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledIntegrationRequiresHttpEndpoint()
    {
        var values = new WmsSettingsValues
        {
            Integrations = new WmsIntegrationSettings
            {
                Enabled = true,
                EndpointUrl = "file:///unsafe"
            }
        };

        var errors = WmsSettingsValidation.Validate(values);

        errors.Should().ContainKey("Integrations.EndpointUrl");
    }

    [Fact]
    public void InvalidTimeZoneIsRejectedBeforePersistence()
    {
        var values = new WmsSettingsValues
        {
            Localization = new WmsLocalizationSettings
            {
                TimeZone = "Not/A-Real-TimeZone"
            }
        };

        var errors = WmsSettingsValidation.Validate(values);

        errors.Should().ContainKey("Localization.TimeZone");
    }
}
