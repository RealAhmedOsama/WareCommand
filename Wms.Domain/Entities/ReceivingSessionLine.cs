using Wms.Domain.Common;

namespace Wms.Domain.Entities;

public sealed class ReceivingSessionLine : Entity
{
    private ReceivingSessionLine()
    {
    }

    public ReceivingSessionLine(
        ReceivingSession session,
        int lineNumber,
        int itemId,
        string itemSkuSnapshot,
        string itemNameSnapshot,
        string baseUnitOfMeasure,
        decimal? expectedBaseQuantity,
        decimal overDeliveryTolerancePercent = 0m,
        decimal underDeliveryTolerancePercent = 0m,
        int? purchaseOrderLineId = null,
        int? advanceShippingNoticeLineId = null,
        decimal previouslyReceivedBaseQuantity = 0m,
        string? expectedLotNumber = null,
        DateTime? expectedExpiryDate = null,
        string? expectedSerialNumber = null,
        string? expectedLicensePlateNumber = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ItemSkuSnapshot = Required(itemSkuSnapshot, 50, nameof(itemSkuSnapshot));
        ItemNameSnapshot = Required(itemNameSnapshot, 200, nameof(itemNameSnapshot));
        BaseUnitOfMeasure = Required(baseUnitOfMeasure, 20, nameof(baseUnitOfMeasure)).ToUpperInvariant();
        if (expectedBaseQuantity is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedBaseQuantity));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(previouslyReceivedBaseQuantity, 0m);

        if (overDeliveryTolerancePercent is < 0m or > 100m || underDeliveryTolerancePercent is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(overDeliveryTolerancePercent), "Tolerance must be between 0 and 100 percent.");
        }

        if (purchaseOrderLineId is <= 0 || advanceShippingNoticeLineId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchaseOrderLineId));
        }

        Session = session;
        LineNumber = lineNumber;
        ItemId = itemId;
        ItemSkuSnapshot = ItemSkuSnapshot.ToUpperInvariant();
        ExpectedBaseQuantity = expectedBaseQuantity;
        OverDeliveryTolerancePercent = overDeliveryTolerancePercent;
        UnderDeliveryTolerancePercent = underDeliveryTolerancePercent;
        PurchaseOrderLineId = purchaseOrderLineId;
        AdvanceShippingNoticeLineId = advanceShippingNoticeLineId;
        PreviouslyReceivedBaseQuantity = previouslyReceivedBaseQuantity;
        ExpectedLotNumber = OptionalUpper(expectedLotNumber, 100);
        ExpectedExpiryDate = expectedExpiryDate.HasValue
            ? DateTime.SpecifyKind(expectedExpiryDate.Value, DateTimeKind.Utc)
            : null;
        ExpectedSerialNumber = OptionalUpper(expectedSerialNumber, 100);
        ExpectedLicensePlateNumber = OptionalUpper(expectedLicensePlateNumber, 100);
    }

    public int ReceivingSessionId { get; private set; }
    public int LineNumber { get; private set; }
    public int ItemId { get; private set; }
    public string ItemSkuSnapshot { get; private set; } = string.Empty;
    public string ItemNameSnapshot { get; private set; } = string.Empty;
    public string BaseUnitOfMeasure { get; private set; } = string.Empty;
    public decimal? ExpectedBaseQuantity { get; private set; }
    public decimal PreviouslyReceivedBaseQuantity { get; private set; }
    public decimal ReceivedBaseQuantity { get; private set; }
    public decimal OverDeliveryTolerancePercent { get; private set; }
    public decimal UnderDeliveryTolerancePercent { get; private set; }
    public int? PurchaseOrderLineId { get; private set; }
    public int? AdvanceShippingNoticeLineId { get; private set; }
    public string? ExpectedLotNumber { get; private set; }
    public DateTime? ExpectedExpiryDate { get; private set; }
    public string? ExpectedSerialNumber { get; private set; }
    public string? ExpectedLicensePlateNumber { get; private set; }
    public DateTime? LastReceivedAtUtc { get; private set; }
    public long Revision { get; private set; } = 1;

    public ReceivingSession Session { get; private set; } = null!;
    public Item Item { get; private set; } = null!;
    public PurchaseOrderLine? PurchaseOrderLine { get; private set; }
    public AdvanceShippingNoticeLine? AdvanceShippingNoticeLine { get; private set; }
    public ICollection<ReceivingSessionScan> Scans { get; private set; } = new List<ReceivingSessionScan>();

    public decimal? RemainingBaseQuantity => ExpectedBaseQuantity.HasValue
        ? Math.Max(0m, ExpectedBaseQuantity.Value - PreviouslyReceivedBaseQuantity - ReceivedBaseQuantity)
        : null;

    public decimal? MaximumReceivableBaseQuantity => ExpectedBaseQuantity.HasValue
        ? ExpectedBaseQuantity.Value * (1m + OverDeliveryTolerancePercent / 100m)
        : null;

    public bool IsFullyReceived => ExpectedBaseQuantity.HasValue &&
        PreviouslyReceivedBaseQuantity + ReceivedBaseQuantity >= ExpectedBaseQuantity.Value;

    public void EnsureCanRecordScan(decimal baseQuantity)
    {
        if (baseQuantity <= 0m)
        {
            throw new InvalidOperationException("Scan quantity must be positive.");
        }

        if (MaximumReceivableBaseQuantity is { } maximum &&
            PreviouslyReceivedBaseQuantity + ReceivedBaseQuantity + baseQuantity > maximum)
        {
            throw new InvalidOperationException("The scan exceeds the receiving-session line tolerance.");
        }
    }

    public void RecordScan(decimal baseQuantity, DateTime receivedAtUtc)
    {
        EnsureCanRecordScan(baseQuantity);

        ReceivedBaseQuantity += baseQuantity;
        LastReceivedAtUtc = NormalizeUtc(receivedAtUtc);
        Revision++;
        SetUpdatedAt(receivedAtUtc);
    }

    public void ReverseScan(decimal baseQuantity, DateTime correctedAtUtc)
    {
        if (baseQuantity <= 0m || baseQuantity > ReceivedBaseQuantity)
        {
            throw new InvalidOperationException("The correction exceeds the receiving-session line history.");
        }

        ReceivedBaseQuantity -= baseQuantity;
        LastReceivedAtUtc = NormalizeUtc(correctedAtUtc);
        Revision++;
        SetUpdatedAt(correctedAtUtc);
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

    private static string? OptionalUpper(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value)).ToUpperInvariant();

}
