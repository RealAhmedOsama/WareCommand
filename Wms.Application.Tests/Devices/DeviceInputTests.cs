using Wms.Application.Devices;

namespace Wms.Application.Tests.Devices;

public sealed class DeviceInputTests
{
    [Fact]
    public void KeyboardWedgeBufferEmitsNormalizedScanAndSuppressesRapidDuplicate()
    {
        var profile = new WmsScannerProfile
        {
            Name = "dock-wedge",
            MinimumLength = 3,
            MaximumLength = 20,
            Terminator = "\r",
            InterCharacterTimeout = TimeSpan.FromMilliseconds(100),
            DuplicateSuppressionWindow = TimeSpan.FromMilliseconds(500)
        };
        var buffer = new WmsKeyboardWedgeBuffer(profile);
        var start = DateTimeOffset.UtcNow;

        buffer.Append("AB", "DOCK-1", start)
            .Disposition.Should().Be(WmsScanDisposition.Incomplete);
        var accepted = buffer.Append("C\r", "DOCK-1", start.AddMilliseconds(20));

        accepted.Disposition.Should().Be(WmsScanDisposition.Accepted);
        accepted.Scan.Should().NotBeNull();
        accepted.Scan!.Value.Should().Be("ABC");
        accepted.Scan.Source.Should().Be(WmsScanSource.KeyboardWedge);

        var duplicate = buffer.Append("ABC\r", "DOCK-1", start.AddMilliseconds(100));

        duplicate.Disposition.Should().Be(WmsScanDisposition.Duplicate);
        duplicate.ErrorCode.Should().Be("device.scan_duplicate");
    }

    [Fact]
    public void ScannerNormalizerRejectsPartialAndOversizedValuesWithoutParsingInventory()
    {
        var profile = new WmsScannerProfile
        {
            Name = "camera",
            MinimumLength = 4,
            MaximumLength = 8,
            Terminator = string.Empty
        };
        var normalizer = new WmsScanEventNormalizer();
        var timestamp = DateTimeOffset.UtcNow;

        var partial = normalizer.Normalize(
            new WmsRawScanInput("ABC", WmsScanSource.Camera, "PACK-1", timestamp),
            profile);
        var tooLong = normalizer.Normalize(
            new WmsRawScanInput("ABCDEFGHI", WmsScanSource.Camera, "PACK-1", timestamp.AddSeconds(1)),
            profile);

        partial.Disposition.Should().Be(WmsScanDisposition.Incomplete);
        tooLong.Disposition.Should().Be(WmsScanDisposition.Rejected);
        tooLong.ErrorCode.Should().Be("device.scan_too_long");
    }

    [Fact]
    public void ScaleAccumulatorConfirmsStablePrecisionAndSupportsExplicitManualFallback()
    {
        var profile = new WmsScaleProfile
        {
            Unit = "kg",
            RequiredConsecutiveStableSamples = 3,
            MaximumDeviation = .02m,
            DecimalPlaces = 2
        };
        var accumulator = new WmsScaleStabilityAccumulator();
        var start = DateTimeOffset.UtcNow;

        accumulator.Add(new WmsScaleReading(10m, "kg", true, start), profile)
            .Disposition.Should().Be(WmsScaleCaptureDisposition.AwaitingStableWeight);
        accumulator.Add(new WmsScaleReading(10.01m, "kg", true, start.AddMilliseconds(100)), profile)
            .Disposition.Should().Be(WmsScaleCaptureDisposition.AwaitingStableWeight);
        var stable = accumulator.Add(new WmsScaleReading(10m, "kg", true, start.AddMilliseconds(200)), profile);

        stable.Disposition.Should().Be(WmsScaleCaptureDisposition.Accepted);
        stable.Weight.Should().NotBeNull();
        stable.Weight!.Value.Should().Be(10m);
        stable.Weight.SampleCount.Should().Be(3);
        stable.Weight.IsManual.Should().BeFalse();

        var manualProfile = profile with { RequiredConsecutiveStableSamples = 1 };
        var manual = accumulator.AcceptManual(4.25m, "kg", start.AddSeconds(1), manualProfile);

        manual.Disposition.Should().Be(WmsScaleCaptureDisposition.Accepted);
        manual.Weight!.IsManual.Should().BeTrue();
    }

    [Fact]
    public void PrintRouteCatalogPrefersStationAndWarehouseSpecificRoute()
    {
        var catalog = new WmsPrintRouteCatalog(
        [
            new WmsPrintRoute
            {
                Name = "warehouse-default",
                TemplateName = "case-label",
                Format = WmsPrintFormat.Pdf,
                Transport = WmsPrintTransport.BrowserPdf,
                WarehouseId = 7
            },
            new WmsPrintRoute
            {
                Name = "dock-a-labeler",
                TemplateName = "case-label",
                Format = WmsPrintFormat.Pdf,
                Transport = WmsPrintTransport.LocalAdapter,
                WarehouseId = 7,
                StationCode = "DOCK-A",
                AdapterKey = "local-label-adapter"
            }
        ]);

        var resolved = catalog.Resolve(new WmsPrintRequest(
            "case-label",
            WmsPrintFormat.Pdf,
            WarehouseId: 7,
            StationCode: "DOCK-A"));
        var fallback = catalog.Resolve(new WmsPrintRequest(
            "case-label",
            WmsPrintFormat.Pdf,
            WarehouseId: 7,
            StationCode: "DOCK-B"));

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Name.Should().Be("dock-a-labeler");
        fallback.IsSuccess.Should().BeTrue();
        fallback.Value.Name.Should().Be("warehouse-default");
    }

    [Fact]
    public void StationProfileValidationKeepsHardwareConfigurationTyped()
    {
        var profile = new WmsStationDeviceProfile
        {
            StationCode = "DOCK-A",
            WarehouseId = 7,
            DefaultLocationCode = "RECEIVE",
            Scanner = new WmsScannerProfile { Name = "dock-wedge" },
            Scale = new WmsScaleProfile { Unit = "kg" },
            LabelPrinter = new WmsPrintRoute
            {
                Name = "dock-labeler",
                TemplateName = "case-label",
                Format = WmsPrintFormat.Zpl,
                Transport = WmsPrintTransport.NetworkZpl
            }
        };

        profile.Validate().Should().BeEmpty();
    }
}
