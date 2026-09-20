using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class LicensePlateHistory : Entity
{
    private LicensePlateHistory()
    {
    }

    public LicensePlateHistory(
        int licensePlateId,
        LicensePlateHistoryAction action,
        string userId,
        DateTime occurredAtUtc,
        int? itemId = null,
        int? lotId = null,
        int? serialNumberId = null,
        decimal? quantity = null,
        int? fromLocationId = null,
        int? toLocationId = null,
        int? fromLicensePlateId = null,
        int? toLicensePlateId = null,
        string? referenceNumber = null,
        string? reason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(licensePlateId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        if (quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        Action = action;
        LicensePlateId = licensePlateId;
        UserId = userId.Trim();
        OccurredAtUtc = NormalizeUtc(occurredAtUtc);
        ItemId = itemId;
        LotId = lotId;
        SerialNumberId = serialNumberId;
        Quantity = quantity;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        FromLicensePlateId = fromLicensePlateId;
        ToLicensePlateId = toLicensePlateId;
        ReferenceNumber = NormalizeOptional(referenceNumber, 100);
        Reason = NormalizeOptional(reason, 1_000);
    }

    public int LicensePlateId { get; private set; }
    public LicensePlateHistoryAction Action { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }
    public int? ItemId { get; private set; }
    public int? LotId { get; private set; }
    public int? SerialNumberId { get; private set; }
    public decimal? Quantity { get; private set; }
    public int? FromLocationId { get; private set; }
    public int? ToLocationId { get; private set; }
    public int? FromLicensePlateId { get; private set; }
    public int? ToLicensePlateId { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Reason { get; private set; }

    public LicensePlate LicensePlate { get; private set; } = null!;
    public Item? Item { get; private set; }
    public Lot? Lot { get; private set; }
    public SerialNumber? SerialNumber { get; private set; }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
        }

        return normalized;
    }
}
