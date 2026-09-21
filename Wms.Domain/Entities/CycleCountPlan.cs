using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Effective scheduling and scope policy for cycle-count generation. A plan
/// never writes inventory; it only controls the durable count work generated
/// against a snapshot.
/// </summary>
public sealed class CycleCountPlan : Entity
{
    private CycleCountPlan()
    {
    }

    public CycleCountPlan(
        string planKey,
        int warehouseId,
        int? locationId,
        int? itemId,
        string? itemClass,
        int frequencyDays,
        decimal thresholdQuantity,
        bool blind,
        CycleCountFreezePolicy freezePolicy,
        DateTime nextDueAtUtc)
    {
        PlanKey = Required(planKey, 100, nameof(planKey));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (locationId is <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId!.Value);
        }

        if (itemId is <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId!.Value);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequencyDays);

        ArgumentOutOfRangeException.ThrowIfNegative(thresholdQuantity);
        if (!Enum.IsDefined(freezePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(freezePolicy));
        }

        WarehouseId = warehouseId;
        LocationId = locationId;
        ItemId = itemId;
        ItemClass = Optional(itemClass, 20)?.ToUpperInvariant();
        FrequencyDays = frequencyDays;
        ThresholdQuantity = thresholdQuantity;
        Blind = blind;
        FreezePolicy = freezePolicy;
        NextDueAtUtc = Normalize(nextDueAtUtc);
        IsActive = true;
        Revision = 1;
    }

    public string PlanKey { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int? LocationId { get; private set; }
    public int? ItemId { get; private set; }
    public string? ItemClass { get; private set; }
    public int FrequencyDays { get; private set; }
    public decimal ThresholdQuantity { get; private set; }
    public bool Blind { get; private set; }
    public CycleCountFreezePolicy FreezePolicy { get; private set; }
    public DateTime NextDueAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location? Location { get; private set; }
    public Item? Item { get; private set; }
    public ICollection<CycleCountTask> Tasks { get; private set; } = new List<CycleCountTask>();

    public void MarkGenerated(DateTime generatedAtUtc)
    {
        NextDueAtUtc = Normalize(generatedAtUtc).AddDays(FrequencyDays);
        Revision++;
        SetUpdatedAt(generatedAtUtc);
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

    public void Update(
        int? locationId,
        int? itemId,
        string? itemClass,
        int frequencyDays,
        decimal thresholdQuantity,
        bool blind,
        CycleCountFreezePolicy freezePolicy,
        DateTime nextDueAtUtc)
    {
        if (locationId is <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId!.Value);
        }

        if (itemId is <= 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId!.Value);
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequencyDays);

        ArgumentOutOfRangeException.ThrowIfNegative(thresholdQuantity);
        if (!Enum.IsDefined(freezePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(freezePolicy));
        }

        LocationId = locationId;
        ItemId = itemId;
        ItemClass = Optional(itemClass, 20)?.ToUpperInvariant();
        FrequencyDays = frequencyDays;
        ThresholdQuantity = thresholdQuantity;
        Blind = blind;
        FreezePolicy = freezePolicy;
        NextDueAtUtc = Normalize(nextDueAtUtc);
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
            : throw new ArgumentException(
                $"The value cannot exceed {maximumLength} characters.",
                nameof(value));
    }

    private static DateTime Normalize(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
