using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class AdvanceShippingNoticeDiscrepancy : Entity
{
    private AdvanceShippingNoticeDiscrepancy()
    {
    }

    public AdvanceShippingNoticeDiscrepancy(
        int advanceShippingNoticeId,
        int? advanceShippingNoticeLineId,
        AdvanceShippingNoticeDiscrepancyKind kind,
        decimal? expectedBaseQuantity,
        decimal? receivedBaseQuantity,
        decimal? varianceBaseQuantity,
        string details,
        string recordedByUserId,
        DateTime occurredAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(advanceShippingNoticeId);
        if (advanceShippingNoticeLineId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(advanceShippingNoticeLineId));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(details))
        {
            throw new ArgumentException("Discrepancy details are required.", nameof(details));
        }

        if (string.IsNullOrWhiteSpace(recordedByUserId))
        {
            throw new ArgumentException("User ID is required.", nameof(recordedByUserId));
        }

        AdvanceShippingNoticeId = advanceShippingNoticeId;
        AdvanceShippingNoticeLineId = advanceShippingNoticeLineId;
        Kind = kind;
        ExpectedBaseQuantity = expectedBaseQuantity;
        ReceivedBaseQuantity = receivedBaseQuantity;
        VarianceBaseQuantity = varianceBaseQuantity;
        Details = Normalize(details, 2_000);
        RecordedByUserId = recordedByUserId.Trim();
        OccurredAtUtc = NormalizeUtc(occurredAtUtc);
    }

    public int AdvanceShippingNoticeId { get; private set; }
    public int? AdvanceShippingNoticeLineId { get; private set; }
    public AdvanceShippingNoticeDiscrepancyKind Kind { get; private set; }
    public decimal? ExpectedBaseQuantity { get; private set; }
    public decimal? ReceivedBaseQuantity { get; private set; }
    public decimal? VarianceBaseQuantity { get; private set; }
    public string Details { get; private set; } = string.Empty;
    public string RecordedByUserId { get; private set; } = string.Empty;
    public DateTime OccurredAtUtc { get; private set; }

    public AdvanceShippingNotice AdvanceShippingNotice { get; private set; } = null!;
    public AdvanceShippingNoticeLine? AdvanceShippingNoticeLine { get; private set; }

    private static string Normalize(string value, int maximumLength)
    {
        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", nameof(value));
    }
}
