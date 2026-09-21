using System.Text.Json;
using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed record ApprovalLevelDefinition(int Level, IReadOnlyList<string> Roles);

/// <summary>
/// Deterministic operation-specific approval policy. A policy always names an
/// operation; there is intentionally no catch-all policy that applies globally.
/// </summary>
public sealed class ApprovalPolicy : Entity
{
    private ApprovalPolicy()
    {
    }

    public ApprovalPolicy(
        string code,
        string name,
        string module,
        string operation,
        int? warehouseId,
        string? reasonCode,
        int priority,
        decimal? minimumQuantity,
        decimal? minimumValue,
        decimal? minimumVariancePercent,
        string? itemRisk,
        string? statusRisk,
        IReadOnlyList<ApprovalLevelDefinition> levels,
        int expiryMinutes,
        bool requireSeparationOfDuties,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        bool isActive = true)
    {
        Apply(
            code,
            name,
            module,
            operation,
            warehouseId,
            reasonCode,
            priority,
            minimumQuantity,
            minimumValue,
            minimumVariancePercent,
            itemRisk,
            statusRisk,
            levels,
            expiryMinutes,
            requireSeparationOfDuties,
            effectiveFromUtc,
            effectiveToUtc,
            isActive,
            isNew: true);
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Module { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public int? WarehouseId { get; private set; }
    public string? ReasonCode { get; private set; }
    public int Priority { get; private set; }
    public decimal? MinimumQuantity { get; private set; }
    public decimal? MinimumValue { get; private set; }
    public decimal? MinimumVariancePercent { get; private set; }
    public string? ItemRisk { get; private set; }
    public string? StatusRisk { get; private set; }
    public string ApprovalLevelsJson { get; private set; } = "[]";
    public int ExpiryMinutes { get; private set; }
    public bool RequireSeparationOfDuties { get; private set; }
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public IReadOnlyList<ApprovalLevelDefinition> GetApprovalLevels()
    {
        var levels = JsonSerializer.Deserialize<List<ApprovalLevelDefinition>>(ApprovalLevelsJson);
        return levels is { Count: > 0 }
            ? levels
            : throw new InvalidOperationException("The approval policy has no valid approval levels.");
    }

    public bool IsEffective(DateTimeOffset asOfUtc)
    {
        var instant = asOfUtc.ToUniversalTime();
        return IsActive &&
               EffectiveFromUtc <= instant &&
               (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > instant);
    }

    public bool Matches(
        string module,
        string operation,
        int? warehouseId,
        string reasonCode,
        decimal? quantity,
        decimal? value,
        decimal? variancePercent,
        string? itemRisk,
        string? statusRisk,
        DateTimeOffset asOfUtc)
    {
        if (!IsEffective(asOfUtc) ||
            !string.Equals(Module, module.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Operation, operation.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (WarehouseId.HasValue && WarehouseId != warehouseId) ||
            (!string.IsNullOrWhiteSpace(ReasonCode) &&
             !string.Equals(ReasonCode, reasonCode.Trim(), StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(ItemRisk) &&
             !string.Equals(ItemRisk, itemRisk?.Trim(), StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(StatusRisk) &&
             !string.Equals(StatusRisk, statusRisk?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return (!MinimumQuantity.HasValue || quantity.HasValue && quantity.Value >= MinimumQuantity.Value) &&
               (!MinimumValue.HasValue || value.HasValue && value.Value >= MinimumValue.Value) &&
               (!MinimumVariancePercent.HasValue ||
                variancePercent.HasValue && variancePercent.Value >= MinimumVariancePercent.Value);
    }

    public int SpecificityScore(int? warehouseId, string reasonCode, string? itemRisk, string? statusRisk) =>
        (WarehouseId.HasValue && WarehouseId == warehouseId ? 16 : 0) +
        (!string.IsNullOrWhiteSpace(ReasonCode) &&
         string.Equals(ReasonCode, reasonCode, StringComparison.OrdinalIgnoreCase) ? 8 : 0) +
        (!string.IsNullOrWhiteSpace(ItemRisk) &&
         string.Equals(ItemRisk, itemRisk, StringComparison.OrdinalIgnoreCase) ? 4 : 0) +
        (!string.IsNullOrWhiteSpace(StatusRisk) &&
         string.Equals(StatusRisk, statusRisk, StringComparison.OrdinalIgnoreCase) ? 2 : 0) +
        (MinimumQuantity.HasValue ? 1 : 0) +
        (MinimumValue.HasValue ? 1 : 0) +
        (MinimumVariancePercent.HasValue ? 1 : 0);

    public void Update(
        string code,
        string name,
        string module,
        string operation,
        int? warehouseId,
        string? reasonCode,
        int priority,
        decimal? minimumQuantity,
        decimal? minimumValue,
        decimal? minimumVariancePercent,
        string? itemRisk,
        string? statusRisk,
        IReadOnlyList<ApprovalLevelDefinition> levels,
        int expiryMinutes,
        bool requireSeparationOfDuties,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        bool isActive) =>
        Apply(
            code,
            name,
            module,
            operation,
            warehouseId,
            reasonCode,
            priority,
            minimumQuantity,
            minimumValue,
            minimumVariancePercent,
            itemRisk,
            statusRisk,
            levels,
            expiryMinutes,
            requireSeparationOfDuties,
            effectiveFromUtc,
            effectiveToUtc,
            isActive,
            isNew: false);

    public static string SerializeLevels(IReadOnlyList<ApprovalLevelDefinition> levels)
    {
        ValidateLevels(levels);
        return JsonSerializer.Serialize(levels);
    }

    private void Apply(
        string code,
        string name,
        string module,
        string operation,
        int? warehouseId,
        string? reasonCode,
        int priority,
        decimal? minimumQuantity,
        decimal? minimumValue,
        decimal? minimumVariancePercent,
        string? itemRisk,
        string? statusRisk,
        IReadOnlyList<ApprovalLevelDefinition> levels,
        int expiryMinutes,
        bool requireSeparationOfDuties,
        DateTimeOffset effectiveFromUtc,
        DateTimeOffset? effectiveToUtc,
        bool isActive,
        bool isNew)
    {
        if (warehouseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId));
        }

        if (priority < 0 || expiryMinutes is < 1 or > 43_200)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        if (minimumQuantity is < 0 || minimumValue is < 0 || minimumVariancePercent is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumQuantity));
        }

        var start = effectiveFromUtc.ToUniversalTime();
        var end = effectiveToUtc?.ToUniversalTime();
        if (end.HasValue && end.Value <= start)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        Code = Required(code, 80, nameof(code)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        Module = Required(module, 100, nameof(module));
        Operation = Required(operation, 150, nameof(operation));
        ReasonCode = Optional(reasonCode, 80)?.ToUpperInvariant();
        ItemRisk = Optional(itemRisk, 50);
        StatusRisk = Optional(statusRisk, 50);
        ApprovalLevelsJson = SerializeLevels(levels);
        WarehouseId = warehouseId;
        Priority = priority;
        MinimumQuantity = minimumQuantity;
        MinimumValue = minimumValue;
        MinimumVariancePercent = minimumVariancePercent;
        ExpiryMinutes = expiryMinutes;
        RequireSeparationOfDuties = requireSeparationOfDuties;
        EffectiveFromUtc = start;
        EffectiveToUtc = end;
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

    private static void ValidateLevels(IReadOnlyList<ApprovalLevelDefinition> levels)
    {
        if (levels is null || levels.Count == 0 || levels.Count > 10)
        {
            throw new ArgumentException("At least one and no more than ten approval levels are required.", nameof(levels));
        }

        for (var index = 0; index < levels.Count; index++)
        {
            var level = levels[index];
            if (level.Level != index + 1 || level.Roles is null || level.Roles.Count == 0)
            {
                throw new ArgumentException("Approval levels must be sequential and contain at least one role.", nameof(levels));
            }

            foreach (var role in level.Roles)
            {
                if (string.IsNullOrWhiteSpace(role) || role.Trim().Length > 100)
                {
                    throw new ArgumentException("Approval roles must be non-empty and no longer than 100 characters.", nameof(levels));
                }
            }
        }
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
}
