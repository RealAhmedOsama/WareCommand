using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class ShipmentTrackingEvent : Entity
{
    private ShipmentTrackingEvent()
    {
    }

    public ShipmentTrackingEvent(
        int shipmentId,
        string status,
        string? trackingNumber,
        string? providerReference,
        string source,
        DateTime occurredAtUtc,
        string? payloadReference = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shipmentId);
        ShipmentId = shipmentId;
        Status = Required(status, 100, nameof(status));
        TrackingNumber = Optional(trackingNumber, 250);
        ProviderReference = Optional(providerReference, 250);
        Source = Required(source, 50, nameof(source));
        OccurredAtUtc = DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        PayloadReference = Optional(payloadReference, 500);
    }

    public int ShipmentId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? TrackingNumber { get; private set; }
    public string? ProviderReference { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public string? PayloadReference { get; private set; }

    public Shipment Shipment { get; private set; } = null!;

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
