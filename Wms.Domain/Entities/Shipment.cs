using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class Shipment : Entity
{
    private readonly List<ShipmentLine> _lines = [];
    private readonly List<ShipmentPackageLink> _packages = [];
    private readonly List<ShipmentLoad> _loads = [];
    private readonly List<ShipmentTrackingEvent> _trackingEvents = [];

    private Shipment()
    {
    }

    public Shipment(
        string shipmentNumber,
        int warehouseId,
        int? carrierId,
        int? carrierServiceId,
        string? shipToRecipientName,
        string? shipToPhone,
        string? shipToCountryCode,
        string? shipToRegion,
        string? shipToCity,
        string? shipToPostalCode,
        string? shipToAddressLine1,
        string? shipToAddressLine2,
        string? shipToDeliveryInstructions,
        DateTime? plannedShipAtUtc,
        string? externalReference,
        string? createdByUserId)
    {
        ShipmentNumber = Required(shipmentNumber, 80, nameof(shipmentNumber));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (carrierId is <= 0 || carrierServiceId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(carrierId));
        }

        WarehouseId = warehouseId;
        CarrierId = carrierId;
        CarrierServiceId = carrierServiceId;
        ShipToRecipientName = Optional(shipToRecipientName, 200);
        ShipToPhone = Optional(shipToPhone, 50);
        ShipToCountryCode = Optional(shipToCountryCode, 2)?.ToUpperInvariant();
        ShipToRegion = Optional(shipToRegion, 100);
        ShipToCity = Optional(shipToCity, 100);
        ShipToPostalCode = Optional(shipToPostalCode, 30);
        ShipToAddressLine1 = Optional(shipToAddressLine1, 200);
        ShipToAddressLine2 = Optional(shipToAddressLine2, 200);
        ShipToDeliveryInstructions = Optional(shipToDeliveryInstructions, 1_000);
        PlannedShipAtUtc = NormalizeOptionalUtc(plannedShipAtUtc);
        ExternalReference = Optional(externalReference, 150);
        CreatedByUserId = Optional(createdByUserId, 450);
        Status = ShipmentStatus.Planned;
        Revision = 1;
    }

    public string ShipmentNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int? CarrierId { get; private set; }
    public int? CarrierServiceId { get; private set; }
    public ShipmentStatus Status { get; private set; } = ShipmentStatus.Planned;
    public string? ShipToRecipientName { get; private set; }
    public string? ShipToPhone { get; private set; }
    public string? ShipToCountryCode { get; private set; }
    public string? ShipToRegion { get; private set; }
    public string? ShipToCity { get; private set; }
    public string? ShipToPostalCode { get; private set; }
    public string? ShipToAddressLine1 { get; private set; }
    public string? ShipToAddressLine2 { get; private set; }
    public string? ShipToDeliveryInstructions { get; private set; }
    public DateTime? PlannedShipAtUtc { get; private set; }
    public DateTime? ActualShipAtUtc { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? TrackingStatus { get; private set; }
    public DateTime? LastTrackingAtUtc { get; private set; }
    public string? ExceptionReason { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? CreatedByUserId { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Carrier? Carrier { get; private set; }
    public CarrierService? CarrierService { get; private set; }
    public IReadOnlyList<ShipmentLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<ShipmentPackageLink> Packages => _packages.AsReadOnly();
    public IReadOnlyList<ShipmentLoad> Loads => _loads.AsReadOnly();
    public IReadOnlyList<ShipmentTrackingEvent> TrackingEvents => _trackingEvents.AsReadOnly();

    public void AddLine(ShipmentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        EnsureMutable();
        _lines.Add(line);
        Touch();
    }

    public void AddPackage(ShipmentPackageLink package)
    {
        ArgumentNullException.ThrowIfNull(package);
        EnsureMutable();
        _packages.Add(package);
        Touch();
    }

    public void AddLoad(ShipmentLoad load)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (load.ShipmentId != 0 && load.ShipmentId != Id)
        {
            throw new InvalidOperationException("The load belongs to another shipment.");
        }

        _loads.Add(load);
        Touch();
    }

    public void AddTrackingEvent(ShipmentTrackingEvent trackingEvent)
    {
        ArgumentNullException.ThrowIfNull(trackingEvent);
        if (trackingEvent.ShipmentId != 0 && trackingEvent.ShipmentId != Id)
        {
            throw new InvalidOperationException("The tracking event belongs to another shipment.");
        }

        _trackingEvents.Add(trackingEvent);
    }

    public void ReopenLoading()
    {
        if (Status != ShipmentStatus.Loaded)
        {
            throw new InvalidOperationException($"A shipment in {Status} cannot return to loading.");
        }

        Status = ShipmentStatus.Loading;
        Touch();
    }

    public void MarkReady()
    {
        if (Status is not (ShipmentStatus.Planned or ShipmentStatus.Released or ShipmentStatus.Packing))
        {
            throw new InvalidOperationException($"A shipment in {Status} cannot become ready.");
        }

        if (_packages.Count == 0 || _packages.Any(package =>
                package.Status is ShipmentPackageLinkStatus.Cancelled or ShipmentPackageLinkStatus.Shipped))
        {
            throw new InvalidOperationException("A shipment must contain eligible packages before loading.");
        }

        Status = ShipmentStatus.Ready;
        Touch();
    }

    public void BeginLoading()
    {
        if (Status != ShipmentStatus.Ready)
        {
            throw new InvalidOperationException($"A shipment in {Status} cannot begin loading.");
        }

        Status = ShipmentStatus.Loading;
        Touch();
    }

    public void MarkLoaded()
    {
        if (Status != ShipmentStatus.Loading || _packages.Count == 0 ||
            _packages.Any(package => package.Status != ShipmentPackageLinkStatus.Loaded))
        {
            throw new InvalidOperationException("Every shipment package must be loaded before the shipment is loaded.");
        }

        Status = ShipmentStatus.Loaded;
        Touch();
    }

    public void MarkShipped(string userId, DateTime shippedAtUtc)
    {
        if (Status != ShipmentStatus.Loaded)
        {
            throw new InvalidOperationException($"A shipment in {Status} cannot be shipped.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        Status = ShipmentStatus.Shipped;
        ActualShipAtUtc = NormalizeUtc(shippedAtUtc);
        Touch();
    }

    public void MarkDelivered(DateTime deliveredAtUtc)
    {
        if (Status != ShipmentStatus.Shipped)
        {
            throw new InvalidOperationException("Only shipped shipments can be delivered.");
        }

        Status = ShipmentStatus.Delivered;
        LastTrackingAtUtc = NormalizeUtc(deliveredAtUtc);
        Touch();
    }

    public void Close(string userId, DateTime closedAtUtc)
    {
        if (Status is not (ShipmentStatus.Shipped or ShipmentStatus.Delivered))
        {
            throw new InvalidOperationException("Only shipped or delivered shipments can be closed.");
        }

        ClosedByUserId = Required(userId, 450, nameof(userId));
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        Status = ShipmentStatus.Closed;
        Touch();
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Closed)
        {
            throw new InvalidOperationException($"A shipment in {Status} cannot be cancelled.");
        }

        CancelledByUserId = Required(userId, 450, nameof(userId));
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Status = ShipmentStatus.Cancelled;
        Touch();
    }

    public void RecordTracking(string? trackingNumber, string trackingStatus, DateTime occurredAtUtc)
    {
        TrackingNumber = Optional(trackingNumber, 250);
        TrackingStatus = Required(trackingStatus, 100, nameof(trackingStatus));
        LastTrackingAtUtc = NormalizeUtc(occurredAtUtc);
        if (Status == ShipmentStatus.Shipped &&
            trackingStatus.Equals("DELIVERED", StringComparison.OrdinalIgnoreCase))
        {
            Status = ShipmentStatus.Delivered;
        }

        Touch();
    }

    public void ChangeCarrier(int carrierId, int carrierServiceId, string reason)
    {
        EnsureMutable();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrierId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrierServiceId);
        CarrierId = carrierId;
        CarrierServiceId = carrierServiceId;
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        Touch();
    }

    private void EnsureMutable()
    {
        if (Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Closed or ShipmentStatus.Cancelled)
        {
            throw new InvalidOperationException($"A shipment in {Status} cannot be changed.");
        }
    }

    private void Touch()
    {
        Revision++;
        SetUpdatedAt();
    }

    private static DateTime? NormalizeOptionalUtc(DateTime? value) =>
        value.HasValue ? NormalizeUtc(value.Value) : null;

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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value));
}
