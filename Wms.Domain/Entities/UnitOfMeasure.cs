using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class UnitOfMeasure : Entity
{
    private const int MaximumCodeLength = 20;
    private const int MaximumNameLength = 200;
    private const int MaximumSymbolLength = 20;

    private UnitOfMeasure()
    {
    }

    public UnitOfMeasure(
        string code,
        UnitOfMeasureCategory category,
        int precision,
        string symbol,
        string name,
        string? localizedName = null,
        bool isActive = true)
    {
        Code = NormalizeCode(code);
        Category = category;
        Precision = ValidatePrecision(precision);
        Symbol = Normalize(symbol, MaximumSymbolLength, nameof(symbol));
        Name = Normalize(name, MaximumNameLength, nameof(name));
        LocalizedName = NormalizeOptional(localizedName, MaximumNameLength, Name);
        IsActive = isActive;
    }

    public string Code { get; private set; } = string.Empty;
    public UnitOfMeasureCategory Category { get; private set; }
    public int Precision { get; private set; }
    public string Symbol { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string LocalizedName { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public void Update(
        UnitOfMeasureCategory category,
        int precision,
        string symbol,
        string name,
        string? localizedName)
    {
        Category = category;
        Precision = ValidatePrecision(precision);
        Symbol = Normalize(symbol, MaximumSymbolLength, nameof(symbol));
        Name = Normalize(name, MaximumNameLength, nameof(name));
        LocalizedName = NormalizeOptional(localizedName, MaximumNameLength, Name);
        SetUpdatedAt();
    }

    public void Activate()
    {
        IsActive = true;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        SetUpdatedAt();
    }

    private static string NormalizeCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A code is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > MaximumCodeLength)
        {
            throw new ArgumentException(
                $"The code cannot exceed {MaximumCodeLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    private static int ValidatePrecision(int value)
    {
        if (value is < 0 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Precision must be between 0 and 12 decimal places.");
        }

        return value;
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeOptional(
        string? value,
        int maximumLength,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
        }

        return normalized;
    }
}
