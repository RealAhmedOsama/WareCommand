using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class PackingCommand : Entity
{
    private PackingCommand()
    {
    }

    public PackingCommand(
        int? packingSessionId,
        int? shipmentPackageId,
        string operation,
        string idempotencyKey,
        string requestHash,
        string userId,
        DateTime executedAtUtc)
    {
        if (packingSessionId is null == (shipmentPackageId is null))
        {
            throw new ArgumentException(
                "A packing command must target exactly one session or package.",
                nameof(packingSessionId));
        }

        if (packingSessionId is <= 0 || shipmentPackageId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(packingSessionId));
        }

        PackingSessionId = packingSessionId;
        ShipmentPackageId = shipmentPackageId;
        Operation = Required(operation, 100, nameof(operation));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        RequestHash = Required(requestHash, 64, nameof(requestHash));
        UserId = Required(userId, 450, nameof(userId));
        ExecutedAtUtc = NormalizeUtc(executedAtUtc);
    }

    public int? PackingSessionId { get; private set; }
    public int? ShipmentPackageId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public DateTime ExecutedAtUtc { get; private set; }

    public PackingSession? PackingSession { get; private set; }
    public ShipmentPackage? ShipmentPackage { get; private set; }

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
