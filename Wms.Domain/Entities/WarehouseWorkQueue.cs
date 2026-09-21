using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

#pragma warning disable CA1711
public sealed class WarehouseWorkQueue : Entity
{
    private WarehouseWorkQueue()
    {
    }

    public WarehouseWorkQueue(
        int warehouseId,
        string code,
        string name,
        WarehouseWorkType workType,
        int? zoneLocationId,
        int priority,
        int? capacity,
        string? requiredTeamCode,
        WarehouseWorkAssignmentStrategy assignmentStrategy,
        string requiredSkillCodesJson,
        string requiredCertificationCodesJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!Enum.IsDefined(workType) || !Enum.IsDefined(assignmentStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(workType));
        }

        if (zoneLocationId is <= 0 || priority < 0 || capacity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoneLocationId));
        }

        WarehouseId = warehouseId;
        Code = RequiredUpper(code, 50, nameof(code));
        Name = Required(name, 200, nameof(name));
        WorkType = workType;
        ZoneLocationId = zoneLocationId;
        Priority = priority;
        Capacity = capacity;
        RequiredTeamCode = OptionalUpper(requiredTeamCode, 50);
        AssignmentStrategy = assignmentStrategy;
        RequiredSkillCodesJson = RequiredJson(requiredSkillCodesJson, nameof(requiredSkillCodesJson));
        RequiredCertificationCodesJson = RequiredJson(requiredCertificationCodesJson, nameof(requiredCertificationCodesJson));
        IsActive = true;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public WarehouseWorkType WorkType { get; private set; }
    public int? ZoneLocationId { get; private set; }
    public int Priority { get; private set; }
    public int? Capacity { get; private set; }
    public string? RequiredTeamCode { get; private set; }
    public WarehouseWorkAssignmentStrategy AssignmentStrategy { get; private set; }
    public string RequiredSkillCodesJson { get; private set; } = "[]";
    public string RequiredCertificationCodesJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location? ZoneLocation { get; private set; }

    public void Update(
        string name,
        WarehouseWorkType workType,
        int? zoneLocationId,
        int priority,
        int? capacity,
        string? requiredTeamCode,
        WarehouseWorkAssignmentStrategy assignmentStrategy,
        string requiredSkillCodesJson,
        string requiredCertificationCodesJson,
        bool isActive)
    {
        if (!Enum.IsDefined(workType) || !Enum.IsDefined(assignmentStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(workType));
        }

        if (zoneLocationId is <= 0 || priority < 0 || capacity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(zoneLocationId));
        }

        Name = Required(name, 200, nameof(name));
        WorkType = workType;
        ZoneLocationId = zoneLocationId;
        Priority = priority;
        Capacity = capacity;
        RequiredTeamCode = OptionalUpper(requiredTeamCode, 50);
        AssignmentStrategy = assignmentStrategy;
        RequiredSkillCodesJson = RequiredJson(requiredSkillCodesJson, nameof(requiredSkillCodesJson));
        RequiredCertificationCodesJson = RequiredJson(requiredCertificationCodesJson, nameof(requiredCertificationCodesJson));
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

    private static string? OptionalUpper(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized.ToUpperInvariant()
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }

    private static string RequiredJson(string value, string parameterName) =>
        Required(value, 4_000, parameterName);
}
#pragma warning restore CA1711
