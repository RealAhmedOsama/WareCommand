using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Append-only explanation of a classification change or manual override.
/// Calculation metrics are copied here so a later policy recalculation cannot
/// rewrite the evidence behind a previous decision.
/// </summary>
public sealed class InventoryClassificationHistory : Entity
{
    private InventoryClassificationHistory()
    {
    }

    public InventoryClassificationHistory(
        int warehouseId,
        int itemId,
        InventoryClassificationClass? previousClassification,
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
        string reason,
        DateTime changedAtUtc,
        DateTime? manualOverrideExpiresAtUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A history reason is required.", nameof(reason));
        }

        WarehouseId = warehouseId;
        ItemId = itemId;
        PreviousClassification = previousClassification;
        Classification = classification;
        Source = source;
        MetricValue = metricValue;
        CumulativePercent = cumulativePercent;
        ShippedQuantity = shippedQuantity;
        ShippedLineCount = shippedLineCount;
        MovementQuantity = movementQuantity;
        InventoryValue = inventoryValue;
        CriticalityScore = criticalityScore;
        LookbackFromUtc = NormalizeUtc(lookbackFromUtc);
        LookbackToUtc = NormalizeUtc(lookbackToUtc);
        PolicyId = policyId;
        PolicyRevision = policyRevision;
        CalculationInputVersion = NormalizeRequired(
            calculationInputVersion,
            50,
            nameof(calculationInputVersion));
        CalculationRunKey = NormalizeRequired(calculationRunKey, 250, nameof(calculationRunKey));
        Reason = NormalizeRequired(reason, 1_000, nameof(reason));
        ChangedAtUtc = NormalizeUtc(changedAtUtc);
        ManualOverrideExpiresAtUtc = manualOverrideExpiresAtUtc.HasValue
            ? NormalizeUtc(manualOverrideExpiresAtUtc.Value)
            : null;
    }

    public int WarehouseId { get; private set; }
    public int ItemId { get; private set; }
    public InventoryClassificationClass? PreviousClassification { get; private set; }
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
    public string Reason { get; private set; } = string.Empty;
    public DateTime ChangedAtUtc { get; private set; }
    public DateTime? ManualOverrideExpiresAtUtc { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public InventoryClassificationPolicy? Policy { get; private set; }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                parameterName);
    }
}
