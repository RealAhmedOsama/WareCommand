using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ShipmentPackageLink : Entity
{
    private ShipmentPackageLink()
    {
    }

    public ShipmentPackageLink(int shipmentId, int shipmentPackageId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentPackageId);
        ShipmentId = shipmentId;
        ShipmentPackageId = shipmentPackageId;
        Status = ShipmentPackageLinkStatus.Eligible;
        Revision = 1;
    }

    public int ShipmentId { get; private set; }
    public int ShipmentPackageId { get; private set; }
    public int? ShipmentLoadId { get; private set; }
    public ShipmentPackageLinkStatus Status { get; private set; }
    public string? LoadedByUserId { get; private set; }
    public DateTime? LoadedAtUtc { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Shipment Shipment { get; private set; } = null!;
    public ShipmentPackage ShipmentPackage { get; private set; } = null!;
    public ShipmentLoad? ShipmentLoad { get; private set; }

    public void MarkLoaded(int shipmentLoadId, string userId, DateTime loadedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentLoadId);
        if (Status is ShipmentPackageLinkStatus.Shipped or ShipmentPackageLinkStatus.Cancelled)
        {
            throw new InvalidOperationException($"A shipment package in {Status} cannot be loaded.");
        }

        ShipmentLoadId = shipmentLoadId;
        Status = ShipmentPackageLinkStatus.Loaded;
        LoadedByUserId = Required(userId, 450, nameof(userId));
        LoadedAtUtc = NormalizeUtc(loadedAtUtc);
        Touch();
    }

    public void Unload()
    {
        if (Status != ShipmentPackageLinkStatus.Loaded)
        {
            throw new InvalidOperationException("Only a loaded shipment package can be unloaded.");
        }

        ShipmentLoadId = null;
        Status = ShipmentPackageLinkStatus.Unloaded;
        Touch();
    }

    public void MarkShipped(DateTime shippedAtUtc)
    {
        if (Status != ShipmentPackageLinkStatus.Loaded)
        {
            throw new InvalidOperationException("Only a loaded shipment package can be shipped.");
        }

        Status = ShipmentPackageLinkStatus.Shipped;
        ShippedAtUtc = NormalizeUtc(shippedAtUtc);
        Touch();
    }

    public void Cancel()
    {
        if (Status == ShipmentPackageLinkStatus.Shipped)
        {
            throw new InvalidOperationException("A shipped shipment package cannot be cancelled.");
        }

        Status = ShipmentPackageLinkStatus.Cancelled;
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
}
