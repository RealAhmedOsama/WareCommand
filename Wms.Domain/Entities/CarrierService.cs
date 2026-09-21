using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class CarrierService : Entity
{
    private CarrierService()
    {
    }

    public CarrierService(
        int carrierId,
        string code,
        string name,
        bool supportsTracking = true,
        bool supportsLabel = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(carrierId);
        CarrierId = carrierId;
        Code = Required(code, 80, nameof(code)).ToUpperInvariant();
        Name = Required(name, 200, nameof(name));
        SupportsTracking = supportsTracking;
        SupportsLabel = supportsLabel;
        IsActive = true;
        Revision = 1;
    }

    public int CarrierId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool SupportsTracking { get; private set; }
    public bool SupportsLabel { get; private set; }
    public bool IsActive { get; private set; }
    public long Revision { get; private set; }

    public Carrier Carrier { get; private set; } = null!;

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
