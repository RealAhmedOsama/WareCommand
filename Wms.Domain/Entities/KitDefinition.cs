using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class KitDefinition : Entity
{
    private readonly List<KitDefinitionLine> _lines = [];

    private KitDefinition()
    {
    }

    public KitDefinition(
        string code,
        int version,
        int outputItemId,
        string outputUnitOfMeasure,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null,
        string? instructions = null,
        string? localizedInstructions = null)
    {
        Code = Required(code, 80, nameof(code)).ToUpperInvariant();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputItemId);
        Version = version;
        OutputItemId = outputItemId;
        OutputUnitOfMeasure = Required(outputUnitOfMeasure, 20, nameof(outputUnitOfMeasure)).ToUpperInvariant();
        EffectiveFromUtc = NormalizeUtc(effectiveFromUtc);
        EffectiveToUtc = effectiveToUtc.HasValue ? NormalizeUtc(effectiveToUtc.Value) : null;
        if (EffectiveToUtc.HasValue && EffectiveToUtc <= EffectiveFromUtc)
        {
            throw new ArgumentException(
                "The effective end must be after the effective start.",
                nameof(effectiveToUtc));
        }

        Instructions = Optional(instructions, 8_000);
        LocalizedInstructions = Optional(localizedInstructions, 8_000);
        IsActive = true;
        Revision = 1;
    }

    public string Code { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public int OutputItemId { get; private set; }
    public string OutputUnitOfMeasure { get; private set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public string? Instructions { get; private set; }
    public string? LocalizedInstructions { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Item OutputItem { get; private set; } = null!;
    public ICollection<KitDefinitionLine> Lines => _lines;

    public void AddLine(KitDefinitionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (_lines.Any(existing => existing.Sequence == line.Sequence))
        {
            throw new InvalidOperationException("Kit definition line sequences must be unique.");
        }

        if (line.KitDefinitionId != 0 && line.KitDefinitionId != Id)
        {
            throw new InvalidOperationException("The kit line already belongs to another definition.");
        }

        _lines.Add(line);
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");

}
