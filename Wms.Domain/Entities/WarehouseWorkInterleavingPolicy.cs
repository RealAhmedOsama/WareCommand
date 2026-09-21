using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Transparent weights for deterministic next-work recommendations.
/// </summary>
public sealed class WarehouseWorkInterleavingPolicy : Entity
{
    private WarehouseWorkInterleavingPolicy()
    {
    }

    public WarehouseWorkInterleavingPolicy(
        int warehouseId,
        string code,
        string name,
        decimal priorityWeight,
        decimal deadlineWeight,
        decimal travelWeight,
        decimal zoneAffinityWeight,
        decimal? maximumTravelMinutes,
        bool allowCrossWorkType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ValidateWeights(priorityWeight, deadlineWeight, travelWeight, zoneAffinityWeight, maximumTravelMinutes);

        WarehouseId = warehouseId;
        Code = RequiredUpper(code, 50, nameof(code));
        Name = Required(name, 200, nameof(name));
        PriorityWeight = priorityWeight;
        DeadlineWeight = deadlineWeight;
        TravelWeight = travelWeight;
        ZoneAffinityWeight = zoneAffinityWeight;
        MaximumTravelMinutes = maximumTravelMinutes;
        AllowCrossWorkType = allowCrossWorkType;
        IsActive = true;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal PriorityWeight { get; private set; }
    public decimal DeadlineWeight { get; private set; }
    public decimal TravelWeight { get; private set; }
    public decimal ZoneAffinityWeight { get; private set; }
    public decimal? MaximumTravelMinutes { get; private set; }
    public bool AllowCrossWorkType { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;

    public void Update(
        string code,
        string name,
        decimal priorityWeight,
        decimal deadlineWeight,
        decimal travelWeight,
        decimal zoneAffinityWeight,
        decimal? maximumTravelMinutes,
        bool allowCrossWorkType,
        bool isActive)
    {
        ValidateWeights(priorityWeight, deadlineWeight, travelWeight, zoneAffinityWeight, maximumTravelMinutes);
        Code = RequiredUpper(code, 50, nameof(code));
        Name = Required(name, 200, nameof(name));
        PriorityWeight = priorityWeight;
        DeadlineWeight = deadlineWeight;
        TravelWeight = travelWeight;
        ZoneAffinityWeight = zoneAffinityWeight;
        MaximumTravelMinutes = maximumTravelMinutes;
        AllowCrossWorkType = allowCrossWorkType;
        IsActive = isActive;
        Revision++;
        SetUpdatedAt();
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Revision++;
        SetUpdatedAt();
    }

    private static void ValidateWeights(
        decimal priorityWeight,
        decimal deadlineWeight,
        decimal travelWeight,
        decimal zoneAffinityWeight,
        decimal? maximumTravelMinutes)
    {
        if (priorityWeight < 0m || deadlineWeight < 0m || travelWeight < 0m || zoneAffinityWeight < 0m ||
            maximumTravelMinutes is <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(priorityWeight));
        }

        if (priorityWeight == 0m && deadlineWeight == 0m && travelWeight == 0m && zoneAffinityWeight == 0m)
        {
            throw new ArgumentException("At least one interleaving weight must be positive.");
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

    private static string RequiredUpper(string value, int maximumLength, string parameterName) =>
        Required(value, maximumLength, parameterName).ToUpperInvariant();
}
