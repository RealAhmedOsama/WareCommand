using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ShipmentLoad : Entity
{
    private ShipmentLoad()
    {
    }

    public ShipmentLoad(
        int shipmentId,
        int? dockLocationId,
        string? trailerNumber,
        string? routeReference,
        DateTime openedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentId);
        if (dockLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dockLocationId));
        }

        ShipmentId = shipmentId;
        DockLocationId = dockLocationId;
        TrailerNumber = Optional(trailerNumber, 100);
        RouteReference = Optional(routeReference, 150);
        OpenedAtUtc = DateTime.SpecifyKind(openedAtUtc, DateTimeKind.Utc);
        Status = ShipmentLoadStatus.Open;
        Revision = 1;
    }

    public int ShipmentId { get; private set; }
    public int? DockLocationId { get; private set; }
    public string? TrailerNumber { get; private set; }
    public string? RouteReference { get; private set; }
    public ShipmentLoadStatus Status { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Shipment Shipment { get; private set; } = null!;
    public Location? DockLocation { get; private set; }

    public void Close(DateTime closedAtUtc)
    {
        if (Status != ShipmentLoadStatus.Open)
        {
            throw new InvalidOperationException($"A load in {Status} cannot be closed.");
        }

        Status = ShipmentLoadStatus.Closed;
        ClosedAtUtc = DateTime.SpecifyKind(closedAtUtc, DateTimeKind.Utc);
        Touch();
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
}
