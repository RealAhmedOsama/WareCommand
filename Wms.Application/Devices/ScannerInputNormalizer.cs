using System.Text;

namespace Wms.Application.Devices;

/// <summary>
/// Converts adapter-specific scan payloads into one deterministic event shape.
/// It deliberately does not resolve the value against inventory; that remains
/// a server-authoritative identification/workflow operation.
/// </summary>
public sealed class WmsScanEventNormalizer
{
    private string? _lastAcceptedValue;
    private DateTimeOffset? _lastAcceptedAtUtc;

    public WmsScanNormalizationResult Normalize(
        WmsRawScanInput input,
        WmsScannerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);

        var profileErrors = profile.Validate();
        if (profileErrors.Count > 0)
        {
            return Rejected(
                "device.scanner_profile_invalid",
                "The scanner profile is invalid.");
        }

        if (string.IsNullOrWhiteSpace(input.StationCode))
        {
            return Rejected(
                "device.station_required",
                "A station code is required for scanner input.");
        }

        var value = input.Value.Trim();
        if (!string.IsNullOrEmpty(profile.Prefix) &&
            value.StartsWith(profile.Prefix, StringComparison.Ordinal))
        {
            value = value[profile.Prefix.Length..];
        }

        if (!string.IsNullOrEmpty(profile.Terminator) &&
            value.EndsWith(profile.Terminator, StringComparison.Ordinal))
        {
            value = value[..^profile.Terminator.Length];
        }

        value = value.Trim();
        if (value.Any(char.IsControl))
        {
            return Rejected(
                "device.scan_control_character",
                "The scan contains an unsupported control character.");
        }

        if (value.Length < profile.MinimumLength)
        {
            return new WmsScanNormalizationResult(
                WmsScanDisposition.Incomplete,
                ErrorCode: "device.scan_incomplete",
                Message: "The scan is shorter than the configured minimum length.");
        }

        if (value.Length > profile.MaximumLength)
        {
            return Rejected(
                "device.scan_too_long",
                "The scan exceeds the configured maximum length.");
        }

        if (_lastAcceptedValue is not null &&
            _lastAcceptedAtUtc.HasValue &&
            input.CapturedAtUtc >= _lastAcceptedAtUtc.Value &&
            input.CapturedAtUtc - _lastAcceptedAtUtc.Value <= profile.DuplicateSuppressionWindow &&
            string.Equals(_lastAcceptedValue, value, StringComparison.Ordinal))
        {
            return new WmsScanNormalizationResult(
                WmsScanDisposition.Duplicate,
                ErrorCode: "device.scan_duplicate",
                Message: "The same scan was received within the duplicate suppression window.");
        }

        var scan = new WmsNormalizedScan(
            Guid.NewGuid().ToString("N"),
            value,
            input.Source,
            input.StationCode.Trim(),
            input.CapturedAtUtc,
            input.Symbology);
        _lastAcceptedValue = value;
        _lastAcceptedAtUtc = input.CapturedAtUtc;
        return new WmsScanNormalizationResult(WmsScanDisposition.Accepted, scan);
    }

    private static WmsScanNormalizationResult Rejected(string code, string message) =>
        new(WmsScanDisposition.Rejected, ErrorCode: code, Message: message);
}

/// <summary>
/// Buffers rapid keyboard-wedge characters and emits a normalized event only
/// on the configured terminator or after an inter-character timeout.
/// </summary>
public sealed class WmsKeyboardWedgeBuffer(WmsScannerProfile profile, WmsScanEventNormalizer? normalizer = null)
{
    private readonly WmsScannerProfile _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    private readonly WmsScanEventNormalizer _normalizer = normalizer ?? new();
    private readonly StringBuilder _buffer = new();
    private DateTimeOffset? _lastCharacterAtUtc;

    public WmsScanBufferResult Append(
        string fragment,
        string stationCode,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Length == 0)
        {
            return Incomplete();
        }

        if (_lastCharacterAtUtc.HasValue &&
            capturedAtUtc - _lastCharacterAtUtc.Value > _profile.InterCharacterTimeout)
        {
            _buffer.Clear();
        }

        _lastCharacterAtUtc = capturedAtUtc;
        _buffer.Append(fragment);
        var maximumBufferedLength = _profile.MaximumLength + (_profile.Prefix?.Length ?? 0) +
            _profile.Terminator.Length + 1;
        if (_buffer.Length > maximumBufferedLength)
        {
            Clear();
            return new WmsScanBufferResult(
                WmsScanDisposition.Rejected,
                ErrorCode: "device.scan_too_long",
                Message: "The buffered scan exceeds the configured maximum length.");
        }

        var content = _buffer.ToString();
        if (!string.IsNullOrEmpty(_profile.Terminator) &&
            content.EndsWith(_profile.Terminator, StringComparison.Ordinal))
        {
            return Emit(content[..^_profile.Terminator.Length], stationCode, capturedAtUtc);
        }

        return Incomplete();
    }

    public WmsScanBufferResult Flush(
        string stationCode,
        DateTimeOffset capturedAtUtc)
    {
        if (_buffer.Length == 0)
        {
            return Incomplete();
        }

        if (_lastCharacterAtUtc.HasValue &&
            capturedAtUtc - _lastCharacterAtUtc.Value < _profile.InterCharacterTimeout)
        {
            return Incomplete();
        }

        return Emit(_buffer.ToString(), stationCode, capturedAtUtc);
    }

    private WmsScanBufferResult Emit(string rawValue, string stationCode, DateTimeOffset capturedAtUtc)
    {
        Clear();
        var normalized = _normalizer.Normalize(
            new WmsRawScanInput(
                rawValue,
                WmsScanSource.KeyboardWedge,
                stationCode,
                capturedAtUtc),
            _profile);
        return new WmsScanBufferResult(
            normalized.Disposition,
            normalized.Scan,
            normalized.ErrorCode,
            normalized.Message);
    }

    private void Clear()
    {
        _buffer.Clear();
        _lastCharacterAtUtc = null;
    }

    private static WmsScanBufferResult Incomplete() =>
        new(WmsScanDisposition.Incomplete, ErrorCode: "device.scan_incomplete");
}
