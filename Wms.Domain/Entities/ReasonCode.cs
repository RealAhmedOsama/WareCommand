using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// A localized, warehouse-aware explanation for a sensitive operation.
/// Reason codes are configuration, not free-form text, so callers can prove
/// why an operation was requested and apply consistent approval rules.
/// </summary>
public sealed class ReasonCode : Entity
{
    private ReasonCode()
    {
    }

    public ReasonCode(
        string code,
        ReasonCodeCategory category,
        string module,
        string operation,
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        bool requiresNotes,
        bool requiresAttachment,
        ReasonCodeSeverity severity,
        int? warehouseId,
        bool isActive = true)
    {
        Apply(
            code,
            category,
            module,
            operation,
            nameEn,
            nameAr,
            descriptionEn,
            descriptionAr,
            effectiveFromUtc,
            effectiveToUtc,
            requiresNotes,
            requiresAttachment,
            severity,
            warehouseId,
            isActive,
            isNew: true);
    }

    public string Code { get; private set; } = string.Empty;
    public ReasonCodeCategory Category { get; private set; }
    public string Module { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public string NameEn { get; private set; } = string.Empty;
    public string NameAr { get; private set; } = string.Empty;
    public string? DescriptionEn { get; private set; }
    public string? DescriptionAr { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public bool RequiresNotes { get; private set; }
    public bool RequiresAttachment { get; private set; }
    public ReasonCodeSeverity Severity { get; private set; }
    public int? WarehouseId { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public bool IsEffective(DateTimeOffset asOfUtc)
    {
        var instant = asOfUtc.ToUniversalTime();
        return IsActive &&
               EffectiveFromUtc <= instant &&
               (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > instant);
    }

    public bool AppliesTo(string module, string operation, int? warehouseId) =>
        MatchesScope(Module, module) &&
        MatchesScope(Operation, operation) &&
        (!WarehouseId.HasValue || WarehouseId == warehouseId);

    public void Update(
        string code,
        ReasonCodeCategory category,
        string module,
        string operation,
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        bool requiresNotes,
        bool requiresAttachment,
        ReasonCodeSeverity severity,
        int? warehouseId,
        bool isActive)
    {
        Apply(
            code,
            category,
            module,
            operation,
            nameEn,
            nameAr,
            descriptionEn,
            descriptionAr,
            effectiveFromUtc,
            effectiveToUtc,
            requiresNotes,
            requiresAttachment,
            severity,
            warehouseId,
            isActive,
            isNew: false);
    }

    private void Apply(
        string code,
        ReasonCodeCategory category,
        string module,
        string operation,
        string nameEn,
        string nameAr,
        string? descriptionEn,
        string? descriptionAr,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        bool requiresNotes,
        bool requiresAttachment,
        ReasonCodeSeverity severity,
        int? warehouseId,
        bool isActive,
        bool isNew)
    {
        if (warehouseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId));
        }

        var start = Normalize(effectiveFromUtc);
        var end = effectiveToUtc?.ToUniversalTime();
        if (end.HasValue && end.Value <= start)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        Code = Required(code, 80, nameof(code)).ToUpperInvariant();
        Module = Required(module, 100, nameof(module));
        Operation = Required(operation, 150, nameof(operation));
        NameEn = Required(nameEn, 200, nameof(nameEn));
        NameAr = Required(nameAr, 200, nameof(nameAr));
        DescriptionEn = Optional(descriptionEn, 1_000);
        DescriptionAr = Optional(descriptionAr, 1_000);
        Category = category;
        EffectiveFromUtc = start;
        EffectiveToUtc = end;
        RequiresNotes = requiresNotes;
        RequiresAttachment = requiresAttachment;
        Severity = severity;
        WarehouseId = warehouseId;
        IsActive = isActive;

        if (isNew)
        {
            Revision = 1;
        }
        else
        {
            Revision++;
            SetUpdatedAt();
        }
    }

    private static bool MatchesScope(string configured, string requested) =>
        string.Equals(configured, "*", StringComparison.Ordinal) ||
        string.Equals(configured, requested.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Required(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }

    private static DateTimeOffset Normalize(DateTimeOffset value) => value.ToUniversalTime();
}
