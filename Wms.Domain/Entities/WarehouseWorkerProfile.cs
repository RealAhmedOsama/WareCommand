using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// Warehouse-operational worker facts only. This deliberately excludes payroll,
/// HR, performance ratings, and device identifiers.
/// </summary>
public sealed class WarehouseWorkerProfile : Entity
{
    private WarehouseWorkerProfile()
    {
    }

    public WarehouseWorkerProfile(
        string userId,
        int warehouseId,
        string? teamCode,
        string? shiftCode,
        DateTime? shiftStartAtUtc,
        DateTime? shiftEndAtUtc,
        string timeZoneId,
        string skillCodesJson,
        string certificationCodesJson,
        string preferredZoneLocationIdsJson)
    {
        UserId = Required(userId, 450, nameof(userId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (shiftStartAtUtc.HasValue != shiftEndAtUtc.HasValue)
        {
            throw new ArgumentException(
                "A shift requires both a start and an end instant.",
                nameof(shiftStartAtUtc));
        }

        if (shiftStartAtUtc.HasValue && shiftEndAtUtc <= shiftStartAtUtc)
        {
            throw new ArgumentException(
                "A shift end must be later than its start.",
                nameof(shiftEndAtUtc));
        }

        WarehouseId = warehouseId;
        TeamCode = OptionalUpper(teamCode, 50);
        ShiftCode = OptionalUpper(shiftCode, 50);
        ShiftStartAtUtc = NormalizeUtc(shiftStartAtUtc);
        ShiftEndAtUtc = NormalizeUtc(shiftEndAtUtc);
        TimeZoneId = Required(timeZoneId, 100, nameof(timeZoneId));
        SkillCodesJson = RequiredJson(skillCodesJson, nameof(skillCodesJson));
        CertificationCodesJson = RequiredJson(certificationCodesJson, nameof(certificationCodesJson));
        PreferredZoneLocationIdsJson = RequiredJson(preferredZoneLocationIdsJson, nameof(preferredZoneLocationIdsJson));
        IsActive = true;
        Revision = 1;
    }

    public string UserId { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public string? TeamCode { get; private set; }
    public string? ShiftCode { get; private set; }
    public DateTime? ShiftStartAtUtc { get; private set; }
    public DateTime? ShiftEndAtUtc { get; private set; }
    public string TimeZoneId { get; private set; } = "UTC";
    public string SkillCodesJson { get; private set; } = "[]";
    public string CertificationCodesJson { get; private set; } = "[]";
    public string PreferredZoneLocationIdsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;

    public void Update(
        string? teamCode,
        string? shiftCode,
        DateTime? shiftStartAtUtc,
        DateTime? shiftEndAtUtc,
        string timeZoneId,
        string skillCodesJson,
        string certificationCodesJson,
        string preferredZoneLocationIdsJson,
        bool isActive)
    {
        if (shiftStartAtUtc.HasValue != shiftEndAtUtc.HasValue)
        {
            throw new ArgumentException(
                "A shift requires both a start and an end instant.",
                nameof(shiftStartAtUtc));
        }

        if (shiftStartAtUtc.HasValue && shiftEndAtUtc <= shiftStartAtUtc)
        {
            throw new ArgumentException(
                "A shift end must be later than its start.",
                nameof(shiftEndAtUtc));
        }

        TeamCode = OptionalUpper(teamCode, 50);
        ShiftCode = OptionalUpper(shiftCode, 50);
        ShiftStartAtUtc = NormalizeUtc(shiftStartAtUtc);
        ShiftEndAtUtc = NormalizeUtc(shiftEndAtUtc);
        TimeZoneId = Required(timeZoneId, 100, nameof(timeZoneId));
        SkillCodesJson = RequiredJson(skillCodesJson, nameof(skillCodesJson));
        CertificationCodesJson = RequiredJson(certificationCodesJson, nameof(certificationCodesJson));
        PreferredZoneLocationIdsJson = RequiredJson(preferredZoneLocationIdsJson, nameof(preferredZoneLocationIdsJson));
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

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? Entity.NormalizeUtc(value.Value) : null;
}
