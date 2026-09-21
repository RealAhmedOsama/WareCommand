namespace Wms.Application.Devices;

/// <summary>
/// Confirms a weight from consecutive stable readings. The accumulator is
/// intentionally stateful per capture session and does not persist readings.
/// </summary>
public sealed class WmsScaleStabilityAccumulator
{
    private readonly List<decimal> _stableSamples = [];

    public WmsScaleCaptureResult Add(
        WmsScaleReading reading,
        WmsScaleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Validate().Count > 0)
        {
            return Rejected(
                "device.scale_profile_invalid",
                "The scale profile is invalid.");
        }

        if (string.IsNullOrWhiteSpace(reading.Unit) ||
            !string.Equals(reading.Unit.Trim(), profile.Unit.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            Reset();
            return Rejected(
                "device.scale_unit_invalid",
                "The scale reading unit does not match the configured scale unit.");
        }

        if ((!profile.AllowNegative && reading.Value < 0m) ||
            reading.Value != decimal.Round(reading.Value, profile.DecimalPlaces, MidpointRounding.AwayFromZero))
        {
            Reset();
            return Rejected(
                "device.scale_precision_invalid",
                "The scale reading is outside the configured sign or precision boundary.");
        }

        if (!reading.IsStable)
        {
            Reset();
            return Awaiting();
        }

        if (_stableSamples.Count > 0 &&
            Math.Abs(reading.Value - _stableSamples[^1]) > profile.MaximumDeviation)
        {
            _stableSamples.Clear();
        }

        _stableSamples.Add(reading.Value);
        if (_stableSamples.Count < profile.RequiredConsecutiveStableSamples)
        {
            return Awaiting();
        }

        var minimum = _stableSamples.Min();
        var maximum = _stableSamples.Max();
        if (maximum - minimum > profile.MaximumDeviation)
        {
            _stableSamples.Clear();
            _stableSamples.Add(reading.Value);
            return Awaiting();
        }

        var average = decimal.Round(
            _stableSamples.Average(),
            profile.DecimalPlaces,
            MidpointRounding.AwayFromZero);
        var result = new WmsScaleCaptureResult(
            WmsScaleCaptureDisposition.Accepted,
            new WmsStableWeight(
                average,
                profile.Unit.Trim(),
                _stableSamples.Count,
                reading.CapturedAtUtc,
                IsManual: false));
        Reset();
        return result;
    }

    public WmsScaleCaptureResult AcceptManual(
        decimal value,
        string unit,
        DateTimeOffset capturedAtUtc,
        WmsScaleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.ManualFallbackAllowed)
        {
            return Rejected(
                "device.scale_manual_disabled",
                "Manual scale entry is disabled for this profile.");
        }

        var result = Add(
            new WmsScaleReading(value, unit, IsStable: true, capturedAtUtc),
            profile);
        if (result.Disposition != WmsScaleCaptureDisposition.Accepted || result.Weight is null)
        {
            return result;
        }

        return result with
        {
            Weight = result.Weight with { IsManual = true }
        };
    }

    private void Reset() => _stableSamples.Clear();

    private static WmsScaleCaptureResult Awaiting() =>
        new(WmsScaleCaptureDisposition.AwaitingStableWeight);

    private static WmsScaleCaptureResult Rejected(string code, string message) =>
        new(WmsScaleCaptureDisposition.Rejected, ErrorCode: code, Message: message);
}
