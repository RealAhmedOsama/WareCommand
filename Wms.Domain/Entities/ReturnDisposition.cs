using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReturnDisposition : Entity
{
    private ReturnDisposition()
    {
    }

    public ReturnDisposition(
        int returnAuthorizationId,
        int returnReceiptId,
        ReturnDispositionKind kind,
        decimal quantity,
        int? destinationLocationId,
        int? destinationInventoryStatusId,
        string reason,
        string userId,
        DateTime disposedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnAuthorizationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(returnReceiptId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quantity, 0m);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ReturnAuthorizationId = returnAuthorizationId;
        ReturnReceiptId = returnReceiptId;
        Kind = kind;
        Quantity = quantity;
        DestinationLocationId = destinationLocationId;
        DestinationInventoryStatusId = destinationInventoryStatusId;
        Reason = Required(reason, 1_000, nameof(reason));
        UserId = Required(userId, 450, nameof(userId));
        DisposedAtUtc = DateTime.SpecifyKind(disposedAtUtc, DateTimeKind.Utc);
    }

    public int ReturnAuthorizationId { get; private set; }
    public int ReturnReceiptId { get; private set; }
    public ReturnDispositionKind Kind { get; private set; }
    public decimal Quantity { get; private set; }
    public int? DestinationLocationId { get; private set; }
    public int? DestinationInventoryStatusId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public DateTime DisposedAtUtc { get; private set; }

    public ReturnAuthorization ReturnAuthorization { get; private set; } = null!;
    public ReturnReceipt ReturnReceipt { get; private set; } = null!;
    public Location? DestinationLocation { get; private set; }

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
