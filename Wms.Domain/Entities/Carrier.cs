using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class Carrier : Entity
{
    private readonly List<CarrierService> _services = [];

    private Carrier()
    {
    }

    public Carrier(string code, string name, string? trackingUrlTemplate = null)
    {
        Code = Required(code, 50, nameof(code)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        TrackingUrlTemplate = Optional(trackingUrlTemplate, 500);
        Status = CarrierStatus.Active;
        Revision = 1;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public CarrierStatus Status { get; private set; } = CarrierStatus.Active;
    public string? TrackingUrlTemplate { get; private set; }
    public long Revision { get; private set; }

    public IReadOnlyList<CarrierService> Services => _services.AsReadOnly();

    public void SetStatus(CarrierStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        Touch();
    }

    public void AddService(CarrierService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service.CarrierId != 0 && service.CarrierId != Id)
        {
            throw new InvalidOperationException("The carrier service belongs to another carrier.");
        }

        _services.Add(service);
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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value));
}
