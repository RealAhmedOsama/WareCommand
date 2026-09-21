using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// A supplier-facing return document. It is deliberately separate from
/// customer returns and never represents a negative receipt.
/// </summary>
public sealed class SupplierReturn : Entity
{
    private readonly List<SupplierReturnLine> _lines = [];

    private SupplierReturn()
    {
    }

    public SupplierReturn(
        string returnNumber,
        int warehouseId,
        int supplierId,
        int stagingLocationId,
        string sourceType,
        string reason,
        string createdByUserId,
        DateTime createdAtUtc,
        int? purchaseOrderId = null,
        int? advanceShippingNoticeId = null,
        int? receiptId = null,
        int? qualityInspectionId = null,
        string? supplierAuthorizationReference = null,
        string? externalReference = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stagingLocationId);
        if (purchaseOrderId is <= 0 || advanceShippingNoticeId is <= 0 ||
            receiptId is <= 0 || qualityInspectionId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchaseOrderId));
        }

        ReturnNumber = Required(returnNumber, 80, nameof(returnNumber));
        WarehouseId = warehouseId;
        SupplierId = supplierId;
        StagingLocationId = stagingLocationId;
        SourceType = Required(sourceType, 40, nameof(sourceType)).ToUpperInvariant();
        Reason = Required(reason, 1_000, nameof(reason));
        CreatedByUserId = Required(createdByUserId, 450, nameof(createdByUserId));
        CreatedAtUtc = NormalizeUtc(createdAtUtc);
        PurchaseOrderId = purchaseOrderId;
        AdvanceShippingNoticeId = advanceShippingNoticeId;
        ReceiptId = receiptId;
        QualityInspectionId = qualityInspectionId;
        SupplierAuthorizationReference = OptionalUpper(supplierAuthorizationReference, 120);
        ExternalReference = OptionalUpper(externalReference, 120);
        Status = SupplierReturnStatus.Draft;
        Revision = 1;
    }

    public string ReturnNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public int SupplierId { get; private set; }
    public int StagingLocationId { get; private set; }
    public string SourceType { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string? SupplierAuthorizationReference { get; private set; }
    public string? ExternalReference { get; private set; }
    public int? PurchaseOrderId { get; private set; }
    public int? AdvanceShippingNoticeId { get; private set; }
    public int? ReceiptId { get; private set; }
    public int? QualityInspectionId { get; private set; }
    public int? WarehouseWorkId { get; private set; }
    public SupplierReturnStatus Status { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public string? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedAtUtc { get; private set; }
    public string? ReleasedByUserId { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public string? PackedByUserId { get; private set; }
    public DateTime? PackedAtUtc { get; private set; }
    public string? CarrierCode { get; private set; }
    public string? TrackingNumber { get; private set; }
    public string? ShippingDocumentReference { get; private set; }
    public string? ShippedByUserId { get; private set; }
    public DateTime? ShippedAtUtc { get; private set; }
    public string? AcknowledgedByUserId { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ExceptionReason { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public Supplier Supplier { get; private set; } = null!;
    public WarehouseWork? WarehouseWork { get; private set; }
    public Location StagingLocation { get; private set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; private set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; private set; }
    public Receipt? Receipt { get; private set; }
    public QualityInspection? QualityInspection { get; private set; }
    public IReadOnlyList<SupplierReturnLine> Lines => _lines.AsReadOnly();

    public decimal RequestedQuantity => _lines.Sum(line => line.RequestedBaseQuantity);
    public decimal StagedQuantity => _lines.Sum(line => line.StagedBaseQuantity);
    public decimal ShippedQuantity => _lines.Sum(line => line.ShippedBaseQuantity);
    public bool IsTerminal => Status is SupplierReturnStatus.Closed or SupplierReturnStatus.Cancelled;

    public void AddLine(SupplierReturnLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != SupplierReturnStatus.Draft)
        {
            throw new InvalidOperationException("Supplier-return lines can only be added while the return is a draft.");
        }

        if (_lines.Any(existing => existing.LineNumber == line.LineNumber))
        {
            throw new InvalidOperationException("Supplier-return line numbers must be unique.");
        }

        if (line.WarehouseId != WarehouseId)
        {
            throw new InvalidOperationException("Supplier-return lines must belong to the return warehouse.");
        }

        _lines.Add(line);
        Touch();
    }

    public void Approve(string userId, DateTime approvedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SupplierReturnStatus.Draft)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be approved.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A supplier return must contain at least one line before approval.");
        }

        Status = SupplierReturnStatus.Approved;
        ApprovedByUserId = userId.Trim();
        ApprovedAtUtc = NormalizeUtc(approvedAtUtc);
        Touch(approvedAtUtc);
    }

    public void Release(int warehouseWorkId, string userId, DateTime releasedAtUtc)
    {
        EnsureUser(userId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseWorkId);
        if (Status != SupplierReturnStatus.Approved)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be released to picking.");
        }

        WarehouseWorkId = warehouseWorkId;
        Status = SupplierReturnStatus.Picking;
        ReleasedByUserId = userId.Trim();
        ReleasedAtUtc = NormalizeUtc(releasedAtUtc);
        Touch(releasedAtUtc);
    }

    public void MarkPacked(string userId, DateTime packedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SupplierReturnStatus.Picking)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be packed.");
        }

        if (_lines.Any(line => line.RemainingToStage > 0m))
        {
            throw new InvalidOperationException("Every approved supplier-return quantity must reach staging before packing.");
        }

        Status = SupplierReturnStatus.Packed;
        PackedByUserId = userId.Trim();
        PackedAtUtc = NormalizeUtc(packedAtUtc);
        Touch(packedAtUtc);
    }

    public void MarkShipped(
        string userId,
        string carrierCode,
        string? trackingNumber,
        string? shippingDocumentReference,
        DateTime shippedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SupplierReturnStatus.Packed)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be shipped.");
        }

        if (_lines.Any(line => line.RemainingToShip > 0m))
        {
            throw new InvalidOperationException("Every packed supplier-return quantity must be shipped before confirmation.");
        }

        CarrierCode = Required(carrierCode, 80, nameof(carrierCode));
        TrackingNumber = Optional(trackingNumber, 120);
        ShippingDocumentReference = Optional(shippingDocumentReference, 120);
        Status = SupplierReturnStatus.Shipped;
        ShippedByUserId = userId.Trim();
        ShippedAtUtc = NormalizeUtc(shippedAtUtc);
        Touch(shippedAtUtc);
    }

    public void Acknowledge(string userId, DateTime acknowledgedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SupplierReturnStatus.Shipped)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be acknowledged.");
        }

        Status = SupplierReturnStatus.Acknowledged;
        AcknowledgedByUserId = userId.Trim();
        AcknowledgedAtUtc = NormalizeUtc(acknowledgedAtUtc);
        Touch(acknowledgedAtUtc);
    }

    public void Close(string userId, DateTime closedAtUtc)
    {
        EnsureUser(userId);
        if (Status != SupplierReturnStatus.Acknowledged)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be closed.");
        }

        Status = SupplierReturnStatus.Closed;
        ClosedByUserId = userId.Trim();
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        Touch(closedAtUtc);
    }

    public void Cancel(string userId, DateTime cancelledAtUtc, string reason)
    {
        EnsureUser(userId);
        if (Status is not (SupplierReturnStatus.Draft or SupplierReturnStatus.Approved))
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be cancelled after release.");
        }

        Status = SupplierReturnStatus.Cancelled;
        CancelledByUserId = userId.Trim();
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        Touch(cancelledAtUtc);
    }

    public void MarkException(string userId, string reason, DateTime occurredAtUtc)
    {
        EnsureUser(userId);
        if (Status is SupplierReturnStatus.Closed or SupplierReturnStatus.Cancelled or SupplierReturnStatus.Exception)
        {
            throw new InvalidOperationException($"A supplier return in {Status} cannot be moved to exception.");
        }

        Status = SupplierReturnStatus.Exception;
        ExceptionReason = Required(reason, 1_000, nameof(reason));
        Touch(occurredAtUtc);
    }

    private void Touch(DateTime? atUtc = null)
    {
        Revision++;
        SetUpdatedAt(atUtc);
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
