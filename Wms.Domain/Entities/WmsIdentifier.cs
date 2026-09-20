using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class WmsIdentifier : Entity
{
    private WmsIdentifier()
    {
    }

    public WmsIdentifier(
        string originalValue,
        string normalizedValue,
        IdentificationKind kind,
        BarcodeSymbology symbology,
        string ownerKey,
        bool isActive = true,
        DateTime? validFromUtc = null,
        DateTime? validToUtc = null,
        string? alias = null)
    {
        OriginalValue = Normalize(originalValue, nameof(originalValue), 200, uppercase: false);
        NormalizedValue = Normalize(normalizedValue, nameof(normalizedValue), 200, uppercase: true);
        OwnerKey = Normalize(ownerKey, nameof(ownerKey), 200, uppercase: true);
        Alias = NormalizeOptional(alias, 100);
        ValidFromUtc = NormalizeUtc(validFromUtc ?? DateTime.UnixEpoch);
        ValidToUtc = validToUtc.HasValue ? NormalizeUtc(validToUtc.Value) : null;
        if (ValidToUtc.HasValue && ValidToUtc.Value < ValidFromUtc)
        {
            throw new ArgumentException("Identifier validity cannot end before it starts.", nameof(validToUtc));
        }

        Kind = kind;
        Symbology = symbology;
        IsActive = isActive;
    }

    public string OriginalValue { get; private set; } = string.Empty;
    public string NormalizedValue { get; private set; } = string.Empty;
    public IdentificationKind Kind { get; private set; }
    public BarcodeSymbology Symbology { get; private set; }
    public string OwnerKey { get; private set; } = string.Empty;
    public string? Alias { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }

    public bool IsResolvableAt(DateTime utcNow) =>
        IsActive &&
        NormalizeUtc(utcNow) >= ValidFromUtc &&
        (!ValidToUtc.HasValue || NormalizeUtc(utcNow) < ValidToUtc.Value);

    public void UpdateLifecycle(
        bool isActive,
        DateTime? validFromUtc = null,
        DateTime? validToUtc = null)
    {
        var from = NormalizeUtc(validFromUtc ?? ValidFromUtc);
        var to = validToUtc.HasValue ? NormalizeUtc(validToUtc.Value) : ValidToUtc;
        if (to.HasValue && to.Value < from)
        {
            throw new ArgumentException("Identifier validity cannot end before it starts.", nameof(validToUtc));
        }

        IsActive = isActive;
        ValidFromUtc = from;
        ValidToUtc = to;
        SetUpdatedAt();
    }

    public void SetAlias(string? alias)
    {
        Alias = NormalizeOptional(alias, 100);
        SetUpdatedAt();
    }

    public void SetOwnerKey(string ownerKey)
    {
        OwnerKey = Normalize(ownerKey, nameof(ownerKey), 200, uppercase: true);
        SetUpdatedAt();
    }

    private static string Normalize(
        string value,
        string parameterName,
        int maximumLength,
        bool uppercase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"The value must contain no more than {maximumLength} printable characters.",
                parameterName);
        }

        return uppercase ? normalized.ToUpperInvariant() : normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Normalize(value, nameof(value), maximumLength, uppercase: false);

}
