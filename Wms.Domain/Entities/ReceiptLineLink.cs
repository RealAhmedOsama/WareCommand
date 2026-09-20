using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReceiptLineLink : Entity
{
    private ReceiptLineLink()
    {
    }

    public ReceiptLineLink(
        ReceiptLine receiptLine,
        ReceiptLineLinkType type,
        string reference,
        string userId,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(receiptLine);
        if (string.IsNullOrWhiteSpace(reference))
        {
            throw new ArgumentException("A related document reference is required.", nameof(reference));
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        ReceiptLine = receiptLine;
        Type = type;
        Reference = reference.Trim();
        UserId = userId.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public int ReceiptLineId { get; private set; }
    public ReceiptLineLinkType Type { get; private set; }
    public string Reference { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public string? Notes { get; private set; }

    public ReceiptLine ReceiptLine { get; private set; } = null!;
}
