using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class KitDefinitionLine : Entity
{
    private KitDefinitionLine()
    {
    }

    public KitDefinitionLine(
        int sequence,
        int componentItemId,
        decimal quantityPerOutput,
        string componentUnitOfMeasure,
        KitSubstitutionPolicy substitutionPolicy = KitSubstitutionPolicy.None,
        string? approvedSubstitutionItemIdsJson = null,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(componentItemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantityPerOutput);
        if (!Enum.IsDefined(substitutionPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(substitutionPolicy));
        }

        Sequence = sequence;
        ComponentItemId = componentItemId;
        QuantityPerOutput = quantityPerOutput;
        ComponentUnitOfMeasure = Required(componentUnitOfMeasure, 20, nameof(componentUnitOfMeasure)).ToUpperInvariant();
        SubstitutionPolicy = substitutionPolicy;
        ApprovedSubstitutionItemIdsJson = Optional(approvedSubstitutionItemIdsJson, 2_000);
        Notes = Optional(notes, 1_000);
    }

    public int KitDefinitionId { get; private set; }
    public int Sequence { get; private set; }
    public int ComponentItemId { get; private set; }
    public decimal QuantityPerOutput { get; private set; }
    public string ComponentUnitOfMeasure { get; private set; } = string.Empty;
    public KitSubstitutionPolicy SubstitutionPolicy { get; private set; }
    public string? ApprovedSubstitutionItemIdsJson { get; private set; }
    public string? Notes { get; private set; }
    public long Revision { get; private set; }

    public KitDefinition Definition { get; private set; } = null!;
    public Item ComponentItem { get; private set; } = null!;

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
