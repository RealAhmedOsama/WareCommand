using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Effective-dated, warehouse-scoped configuration for deterministic ABC
/// classification. It contains no learned model or hidden scoring behavior.
/// </summary>
public sealed class InventoryClassificationPolicy : Entity
{
    private const int MaximumPolicyKeyLength = 80;

    private InventoryClassificationPolicy()
    {
    }

    public InventoryClassificationPolicy(
        int warehouseId,
        string policyKey,
        InventoryClassificationMethod method,
        int lookbackDays,
        decimal aThresholdPercent,
        decimal bThresholdPercent,
        decimal minimumActivityValue,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null)
    {
        ValidateIdentity(warehouseId, policyKey);
        Apply(
            method,
            lookbackDays,
            aThresholdPercent,
            bThresholdPercent,
            minimumActivityValue,
            effectiveFromUtc,
            effectiveToUtc,
            stampUpdatedAt: false);
        WarehouseId = warehouseId;
        PolicyKey = NormalizePolicyKey(policyKey);
        IsActive = true;
    }

    public int WarehouseId { get; private set; }
    public string PolicyKey { get; private set; } = string.Empty;
    public InventoryClassificationMethod Method { get; private set; }
    public int LookbackDays { get; private set; }
    public decimal AThresholdPercent { get; private set; }
    public decimal BThresholdPercent { get; private set; }
    public decimal MinimumActivityValue { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;

    public bool IsEffectiveAt(DateTime utcNow) =>
        IsActive &&
        EffectiveFromUtc <= utcNow &&
        (!EffectiveToUtc.HasValue || EffectiveToUtc.Value > utcNow);

    public void Update(
        string policyKey,
        InventoryClassificationMethod method,
        int lookbackDays,
        decimal aThresholdPercent,
        decimal bThresholdPercent,
        decimal minimumActivityValue,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc = null)
    {
        ValidateIdentity(WarehouseId, policyKey);
        Apply(
            method,
            lookbackDays,
            aThresholdPercent,
            bThresholdPercent,
            minimumActivityValue,
            effectiveFromUtc,
            effectiveToUtc,
            stampUpdatedAt: true);
        PolicyKey = NormalizePolicyKey(policyKey);
        Revision++;
    }

    public void Activate()
    {
        IsActive = true;
        Revision++;
        SetUpdatedAt();
    }

    public void Deactivate()
    {
        IsActive = false;
        Revision++;
        SetUpdatedAt();
    }

    private void Apply(
        InventoryClassificationMethod method,
        int lookbackDays,
        decimal aThresholdPercent,
        decimal bThresholdPercent,
        decimal minimumActivityValue,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        bool stampUpdatedAt)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method));
        }

        if (lookbackDays is < 1 or > 3_650)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lookbackDays),
                "The lookback period must be between 1 and 3650 days.");
        }

        if (aThresholdPercent <= 0m || aThresholdPercent > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aThresholdPercent),
                "The A threshold must be greater than 0 and no more than 100 percent.");
        }

        if (bThresholdPercent <= aThresholdPercent || bThresholdPercent > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bThresholdPercent),
                "The B threshold must be greater than A and no more than 100 percent.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(minimumActivityValue);

        var normalizedFrom = NormalizeUtc(effectiveFromUtc);
        DateTime? normalizedTo = effectiveToUtc.HasValue
            ? NormalizeUtc(effectiveToUtc.Value)
            : null;
        if (normalizedTo.HasValue && normalizedTo.Value <= normalizedFrom)
        {
            throw new ArgumentException(
                "The effective end must be later than the effective start.",
                nameof(effectiveToUtc));
        }

        Method = method;
        LookbackDays = lookbackDays;
        AThresholdPercent = aThresholdPercent;
        BThresholdPercent = bThresholdPercent;
        MinimumActivityValue = minimumActivityValue;
        EffectiveFromUtc = normalizedFrom;
        EffectiveToUtc = normalizedTo;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static void ValidateIdentity(int warehouseId, string policyKey)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        _ = NormalizePolicyKey(policyKey);
    }

    private static string NormalizePolicyKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A policy key is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > MaximumPolicyKeyLength)
        {
            throw new ArgumentException(
                $"A policy key cannot exceed {MaximumPolicyKeyLength} characters.",
                nameof(value));
        }

        return normalized;
    }
}
