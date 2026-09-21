using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// The current warehouse-specific classification for one item. The append-only
/// history entity records every effective class/source change.
/// </summary>
public sealed class InventoryClassification : Entity
{
    private InventoryClassification()
    {
    }

    public InventoryClassification(
        int warehouseId,
        int itemId,
        InventoryClassificationClass classification,
        InventoryClassificationSource source,
        decimal metricValue,
        decimal cumulativePercent,
        decimal shippedQuantity,
        int shippedLineCount,
        decimal movementQuantity,
        decimal inventoryValue,
        decimal criticalityScore,
        DateTime lookbackFromUtc,
        DateTime lookbackToUtc,
        int? policyId,
        long policyRevision,
        string calculationInputVersion,
        string calculationRunKey,
        DateTime calculatedAtUtc,
        string? manualOverrideReason = null,
        DateTime? manualOverrideExpiresAtUtc = null)
    {
        ValidateIdentity(warehouseId, itemId);
        ApplyMetrics(
            classification,
            source,
            metricValue,
            cumulativePercent,
            shippedQuantity,
            shippedLineCount,
            movementQuantity,
            inventoryValue,
            criticalityScore,
            lookbackFromUtc,
            lookbackToUtc,
            policyId,
            policyRevision,
            calculationInputVersion,
            calculationRunKey,
            calculatedAtUtc,
            manualOverrideReason,
            manualOverrideExpiresAtUtc,
            stampUpdatedAt: false);
        WarehouseId = warehouseId;
        ItemId = itemId;
    }

    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public InventoryClassificationClass Classification { get; private set; }
    public InventoryClassificationSource Source { get; private set; }
    public decimal MetricValue { get; private set; }
    public decimal CumulativePercent { get; private set; }
    public decimal ShippedQuantity { get; private set; }
    public int ShippedLineCount { get; private set; }
    public decimal MovementQuantity { get; private set; }
    public decimal InventoryValue { get; private set; }
    public decimal CriticalityScore { get; private set; }
    public DateTime LookbackFromUtc { get; private set; }
    public DateTime LookbackToUtc { get; private set; }
    public int? PolicyId { get; private set; }
    public long PolicyRevision { get; private set; }
    public string CalculationInputVersion { get; private set; } = string.Empty;
    public string CalculationRunKey { get; private set; } = string.Empty;
    public DateTime CalculatedAtUtc { get; private set; }
    public string? ManualOverrideReason { get; private set; }
    public DateTime? ManualOverrideExpiresAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public InventoryClassificationPolicy? Policy { get; private set; }

    public bool HasActiveManualOverride(DateTime utcNow) =>
        Source == InventoryClassificationSource.ManualOverride &&
        (!ManualOverrideExpiresAtUtc.HasValue || ManualOverrideExpiresAtUtc.Value > utcNow);

    public void ApplyAutomatic(
        InventoryClassificationClass classification,
        decimal metricValue,
        decimal cumulativePercent,
        decimal shippedQuantity,
        int shippedLineCount,
        decimal movementQuantity,
        decimal inventoryValue,
        decimal criticalityScore,
        DateTime lookbackFromUtc,
        DateTime lookbackToUtc,
        int? policyId,
        long policyRevision,
        string calculationInputVersion,
        string calculationRunKey,
        DateTime calculatedAtUtc)
    {
        ApplyMetrics(
            classification,
            InventoryClassificationSource.Automatic,
            metricValue,
            cumulativePercent,
            shippedQuantity,
            shippedLineCount,
            movementQuantity,
            inventoryValue,
            criticalityScore,
            lookbackFromUtc,
            lookbackToUtc,
            policyId,
            policyRevision,
            calculationInputVersion,
            calculationRunKey,
            calculatedAtUtc,
            manualOverrideReason: null,
            manualOverrideExpiresAtUtc: null,
            stampUpdatedAt: true);
        Revision++;
    }

    public void ApplyManualOverride(
        InventoryClassificationClass classification,
        string reason,
        DateTime? expiresAtUtc,
        string calculationRunKey,
        DateTime changedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A manual override reason is required.", nameof(reason));
        }

        if (reason.Trim().Length > 1_000)
        {
            throw new ArgumentException(
                "A manual override reason cannot exceed 1000 characters.",
                nameof(reason));
        }

        var normalizedChangedAt = NormalizeUtc(changedAtUtc);
        DateTime? normalizedExpiresAt = expiresAtUtc.HasValue
            ? NormalizeUtc(expiresAtUtc.Value)
            : null;
        if (normalizedExpiresAt.HasValue && normalizedExpiresAt.Value <= normalizedChangedAt)
        {
            throw new ArgumentException(
                "A manual override expiry must be later than the change time.",
                nameof(expiresAtUtc));
        }

        Classification = classification;
        Source = InventoryClassificationSource.ManualOverride;
        ManualOverrideReason = reason.Trim();
        ManualOverrideExpiresAtUtc = normalizedExpiresAt;
        CalculationRunKey = NormalizeRequired(calculationRunKey, 250, nameof(calculationRunKey));
        CalculatedAtUtc = normalizedChangedAt;
        Revision++;
        SetUpdatedAt();
    }

    private void ApplyMetrics(
        InventoryClassificationClass classification,
        InventoryClassificationSource source,
        decimal metricValue,
        decimal cumulativePercent,
        decimal shippedQuantity,
        int shippedLineCount,
        decimal movementQuantity,
        decimal inventoryValue,
        decimal criticalityScore,
        DateTime lookbackFromUtc,
        DateTime lookbackToUtc,
        int? policyId,
        long policyRevision,
        string calculationInputVersion,
        string calculationRunKey,
        DateTime calculatedAtUtc,
        string? manualOverrideReason,
        DateTime? manualOverrideExpiresAtUtc,
        bool stampUpdatedAt)
    {
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (metricValue < 0m || cumulativePercent < 0m || cumulativePercent > 100m ||
            shippedQuantity < 0m || shippedLineCount < 0 || movementQuantity < 0m ||
            inventoryValue < 0m || criticalityScore < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(metricValue));
        }

        var normalizedFrom = NormalizeUtc(lookbackFromUtc);
        var normalizedTo = NormalizeUtc(lookbackToUtc);
        if (normalizedTo < normalizedFrom)
        {
            throw new ArgumentException(
                "The lookback end cannot be earlier than the lookback start.",
                nameof(lookbackToUtc));
        }

        Classification = classification;
        Source = source;
        MetricValue = metricValue;
        CumulativePercent = cumulativePercent;
        ShippedQuantity = shippedQuantity;
        ShippedLineCount = shippedLineCount;
        MovementQuantity = movementQuantity;
        InventoryValue = inventoryValue;
        CriticalityScore = criticalityScore;
        LookbackFromUtc = normalizedFrom;
        LookbackToUtc = normalizedTo;
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        CalculationInputVersion = NormalizeRequired(
            calculationInputVersion,
            50,
            nameof(calculationInputVersion));
        CalculationRunKey = NormalizeRequired(calculationRunKey, 250, nameof(calculationRunKey));
        CalculatedAtUtc = NormalizeUtc(calculatedAtUtc);
        ManualOverrideReason = string.IsNullOrWhiteSpace(manualOverrideReason)
            ? null
            : manualOverrideReason.Trim();
        ManualOverrideExpiresAtUtc = manualOverrideExpiresAtUtc.HasValue
            ? NormalizeUtc(manualOverrideExpiresAtUtc.Value)
            : null;
        if (stampUpdatedAt)
        {
            SetUpdatedAt();
        }
    }

    private static void ValidateIdentity(int warehouseId, int itemId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
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
}
