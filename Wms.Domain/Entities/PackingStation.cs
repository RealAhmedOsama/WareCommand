using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class PackingStation : Entity
{
    private PackingStation()
    {
    }

    public PackingStation(
        string code,
        string name,
        int warehouseId,
        int locationId,
        string? supportedDevices = null,
        string? printerProfile = null,
        string? scaleProfile = null,
        string? allowedUserIds = null,
        string? permissionProfile = null)
    {
        Code = Required(code, 50, nameof(code)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        WarehouseId = warehouseId;
        LocationId = locationId;
        SupportedDevices = Optional(supportedDevices, 2_000);
        PrinterProfile = Optional(printerProfile, 200);
        ScaleProfile = Optional(scaleProfile, 200);
        AllowedUserIds = Optional(allowedUserIds, 4_000);
        PermissionProfile = Optional(permissionProfile, 100);
        Status = PackingStationStatus.Active;
        Revision = 1;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int LocationId { get; private set; }
    public PackingStationStatus Status { get; private set; } = PackingStationStatus.Active;
    public string? SupportedDevices { get; private set; }
    public string? PrinterProfile { get; private set; }
    public string? ScaleProfile { get; private set; }
    public string? AllowedUserIds { get; private set; }
    public string? PermissionProfile { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location Location { get; private set; } = null!;

    public bool IsAvailable => Status == PackingStationStatus.Active;

    public void Update(
        string name,
        int locationId,
        string? supportedDevices,
        string? printerProfile,
        string? scaleProfile,
        string? allowedUserIds,
        string? permissionProfile)
    {
        Name = Required(name, 200, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(locationId);
        LocationId = locationId;
        SupportedDevices = Optional(supportedDevices, 2_000);
        PrinterProfile = Optional(printerProfile, 200);
        ScaleProfile = Optional(scaleProfile, 200);
        AllowedUserIds = Optional(allowedUserIds, 4_000);
        PermissionProfile = Optional(permissionProfile, 100);
        Touch();
    }

    public void SetStatus(PackingStationStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        Touch();
    }

    private void Touch()
    {
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

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }
}
