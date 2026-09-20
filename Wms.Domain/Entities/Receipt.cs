using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// The durable physical receiving document. Inventory movements must point to
/// a persisted receipt line; a PO or ASN reference alone is not a receipt.
/// </summary>
public sealed class Receipt : Entity
{
    private readonly List<ReceiptLine> _lines = [];

    private Receipt()
    {
    }

    public Receipt(
        string documentNumber,
        int warehouseId,
        string warehouseCodeSnapshot,
        int? supplierId,
        string? supplierCodeSnapshot,
        string? supplierNameSnapshot,
        int? purchaseOrderId,
        int? advanceShippingNoticeId,
        int? dockLocationId,
        int receivingLocationId,
        string sourceType = "MANUAL",
        string? sourceReference = null,
        string? externalReference = null,
        string? sessionReference = null,
        string? notes = null,
        string? createdByUserId = null,
        DateTime? receivedAtUtc = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receivingLocationId);
        if (supplierId is <= 0 || purchaseOrderId is <= 0 || advanceShippingNoticeId is <= 0 || dockLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(supplierId), "Optional identifiers must be positive when supplied.");
        }

        DocumentNumber = Required(documentNumber, 50, nameof(documentNumber));
        WarehouseId = warehouseId;
        WarehouseCodeSnapshot = Required(warehouseCodeSnapshot, 20, nameof(warehouseCodeSnapshot));
        SupplierId = supplierId;
        SupplierCodeSnapshot = Optional(supplierCodeSnapshot, 50);
        SupplierNameSnapshot = Optional(supplierNameSnapshot, 200);
        PurchaseOrderId = purchaseOrderId;
        AdvanceShippingNoticeId = advanceShippingNoticeId;
        DockLocationId = dockLocationId;
        ReceivingLocationId = receivingLocationId;
        SourceType = Required(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = Optional(sourceReference, 200);
        ExternalReference = OptionalUpper(externalReference, 100);
        SessionReference = Optional(sessionReference, 100);
        Notes = Optional(notes, 2_000);
        CreatedByUserId = Optional(createdByUserId, 450);
        ReceivedAtUtc = receivedAtUtc.HasValue ? NormalizeUtc(receivedAtUtc.Value) : null;
    }

    public string DocumentNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public string WarehouseCodeSnapshot { get; private set; } = string.Empty;
    public int? SupplierId { get; private set; }
    public string? SupplierCodeSnapshot { get; private set; }
    public string? SupplierNameSnapshot { get; private set; }
    public int? PurchaseOrderId { get; private set; }
    public int? AdvanceShippingNoticeId { get; private set; }
    public int? DockLocationId { get; private set; }
    public int ReceivingLocationId { get; private set; }
    public string SourceType { get; private set; } = "MANUAL";
    public string? SourceReference { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? SessionReference { get; private set; }
    public string? Notes { get; private set; }
    public ReceiptStatus Status { get; private set; } = ReceiptStatus.Draft;
    public string? CreatedByUserId { get; private set; }
    public DateTime? ReceivedAtUtc { get; private set; }
    public string? OpenedByUserId { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public string? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ReversedByUserId { get; private set; }
    public DateTime? ReversedAtUtc { get; private set; }
    public string? CorrectedByUserId { get; private set; }
    public DateTime? CorrectedAtUtc { get; private set; }
    public int? CorrectedByReceiptId { get; private set; }
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;
    public Supplier? Supplier { get; private set; }
    public PurchaseOrder? PurchaseOrder { get; private set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; private set; }
    public Location? DockLocation { get; private set; }
    public Location ReceivingLocation { get; private set; } = null!;
    public Receipt? CorrectedReceipt { get; private set; }
    public IReadOnlyList<ReceiptLine> Lines => _lines.AsReadOnly();

    public bool HasMovementHistory => _lines.Any(line => line.HasMovementHistory);
    public bool CanEdit => (Status is ReceiptStatus.Draft or ReceiptStatus.Open) && !HasMovementHistory;
    public bool CanReceive => Status is ReceiptStatus.Draft or ReceiptStatus.Open or ReceiptStatus.Receiving or ReceiptStatus.Exception;
    public bool CanCancel => !HasMovementHistory &&
        (Status is ReceiptStatus.Draft or ReceiptStatus.Open or ReceiptStatus.Exception);
    public bool CanReverse => HasMovementHistory &&
        (Status is ReceiptStatus.Completed or ReceiptStatus.PartiallyCompleted or ReceiptStatus.Exception);
    public bool CanCorrect => CorrectedByReceiptId is null &&
        (CanReverse || Status == ReceiptStatus.Reversed);

    public void AddLine(ReceiptLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!CanEdit)
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot add lines.");
        }

        if (line.WarehouseId != WarehouseId)
        {
            throw new InvalidOperationException("Receipt lines must belong to the receipt warehouse.");
        }

        if (_lines.Any(existing => existing.LineNumber == line.LineNumber))
        {
            throw new InvalidOperationException("Receipt line numbers must be unique.");
        }

        _lines.Add(line);
        Revision++;
        SetUpdatedAt();
    }

    public void Open(string userId, DateTime openedAtUtc)
    {
        EnsureUser(userId);
        if (Status != ReceiptStatus.Draft)
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot be opened.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A receipt must contain at least one line before opening.");
        }

        Status = ReceiptStatus.Open;
        OpenedByUserId = userId.Trim();
        OpenedAtUtc = NormalizeUtc(openedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void StartReceiving(string userId, DateTime receivedAtUtc)
    {
        EnsureUser(userId);
        if (!CanReceive)
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot receive stock.");
        }

        Status = ReceiptStatus.Receiving;
        ReceivedAtUtc ??= NormalizeUtc(receivedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void Complete(string userId, DateTime completedAtUtc)
    {
        EnsureUser(userId);
        if (Status is not (ReceiptStatus.Receiving or ReceiptStatus.Exception))
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot be completed.");
        }

        if (_lines.Count == 0 || _lines.Any(line => !line.HasReceiptHistory))
        {
            throw new InvalidOperationException("A receipt cannot be completed before each line has physical receipt history.");
        }

        Status = _lines.All(line => line.IsFullyReceived)
            ? ReceiptStatus.Completed
            : ReceiptStatus.PartiallyCompleted;
        CompletedByUserId = userId.Trim();
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void MarkException()
    {
        if (Status is not (ReceiptStatus.Receiving or ReceiptStatus.PartiallyCompleted or ReceiptStatus.Completed))
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot be marked as an exception.");
        }

        Status = ReceiptStatus.Exception;
        Revision++;
        SetUpdatedAt();
    }

    public void Cancel(string userId, DateTime cancelledAtUtc)
    {
        EnsureUser(userId);
        if (!CanCancel)
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot be cancelled after movement history exists.");
        }

        Status = ReceiptStatus.Cancelled;
        CancelledByUserId = userId.Trim();
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void MarkReversed(string userId, DateTime reversedAtUtc)
    {
        EnsureUser(userId);
        if (!CanReverse)
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot be reversed.");
        }

        Status = ReceiptStatus.Reversed;
        ReversedByUserId = userId.Trim();
        ReversedAtUtc = NormalizeUtc(reversedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void MarkCorrected(string userId, int correctedByReceiptId, DateTime correctedAtUtc)
    {
        EnsureUser(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(correctedByReceiptId);
        if (!CanCorrect)
        {
            throw new InvalidOperationException($"A receipt in {Status} cannot be corrected.");
        }

        Status = ReceiptStatus.Corrected;
        CorrectedByUserId = userId.Trim();
        CorrectedAtUtc = NormalizeUtc(correctedAtUtc);
        CorrectedByReceiptId = correctedByReceiptId;
        Revision++;
        SetUpdatedAt();
    }

    private static void EnsureUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }
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

    private static string? Optional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : throw new ArgumentException($"The value cannot exceed {maximumLength} characters.");
    }

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();
}
