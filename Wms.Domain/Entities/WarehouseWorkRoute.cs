using Wms.Domain.Common;

namespace Wms.Domain.Entities;

/// <summary>
/// A directed, supervisor-maintained travel edge between warehouse locations.
/// The recommendation engine only uses these explicit costs; it does not infer
/// opaque routes from operator behavior.
/// </summary>
public sealed class WarehouseWorkRoute : Entity
{
    private WarehouseWorkRoute()
    {
    }

    public WarehouseWorkRoute(
        int warehouseId,
        int fromLocationId,
        int toLocationId,
        string routeCode,
        int sequence,
        decimal travelMinutes,
        decimal? distanceMeters,
        string? notes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toLocationId);
        if (fromLocationId == toLocationId)
        {
            throw new ArgumentException("A route must connect two different locations.", nameof(toLocationId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegative(travelMinutes);
        if (distanceMeters is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }

        WarehouseId = warehouseId;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        RouteCode = RequiredUpper(routeCode, 50, nameof(routeCode));
        Sequence = sequence;
        TravelMinutes = travelMinutes;
        DistanceMeters = distanceMeters;
        Notes = Optional(notes, 1_000);
        IsActive = true;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public int FromLocationId { get; private set; }
    public int ToLocationId { get; private set; }
    public string RouteCode { get; private set; } = string.Empty;
    public int Sequence { get; private set; }
    public decimal TravelMinutes { get; private set; }
    public decimal? DistanceMeters { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Location FromLocation { get; private set; } = null!;
    public Location ToLocation { get; private set; } = null!;

    public void Update(
        int fromLocationId,
        int toLocationId,
        string routeCode,
        int sequence,
        decimal travelMinutes,
        decimal? distanceMeters,
        string? notes,
        bool isActive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromLocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toLocationId);
        if (fromLocationId == toLocationId)
        {
            throw new ArgumentException("A route must connect two different locations.", nameof(toLocationId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentOutOfRangeException.ThrowIfNegative(travelMinutes);
        if (distanceMeters is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }

        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        RouteCode = RequiredUpper(routeCode, 50, nameof(routeCode));
        Sequence = sequence;
        TravelMinutes = travelMinutes;
        DistanceMeters = distanceMeters;
        Notes = Optional(notes, 1_000);
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

    private static string RequiredUpper(string value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized.ToUpperInvariant()
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
