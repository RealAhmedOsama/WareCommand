using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Expected inbound demand. The order keeps master-data snapshots and only
/// changes its lifecycle/receipt ledger after confirmation.
/// </summary>
public sealed class PurchaseOrder : Entity
{
    private readonly List<PurchaseOrderLine> _lines = new();

    private PurchaseOrder()
    {
    }

    public PurchaseOrder(
        string documentNumber,
        int warehouseId,
        string warehouseCodeSnapshot,
        int supplierId,
        string supplierCodeSnapshot,
        string supplierNameSnapshot,
        DateOnly orderDate,
        DateOnly? expectedReceiptDate = null,
        string? externalReference = null,
        string sourceType = "MANUAL",
        string? sourceReference = null,
        string? currencyCodeSnapshot = null,
        string? notes = null,
        string? createdByUserId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        DocumentNumber = NormalizeRequired(documentNumber, 50, nameof(documentNumber));
        WarehouseId = warehouseId;
        WarehouseCodeSnapshot = NormalizeRequired(warehouseCodeSnapshot, 20, nameof(warehouseCodeSnapshot));
        SupplierId = supplierId;
        SupplierCodeSnapshot = NormalizeRequired(supplierCodeSnapshot, 50, nameof(supplierCodeSnapshot));
        SupplierNameSnapshot = NormalizeRequired(supplierNameSnapshot, 200, nameof(supplierNameSnapshot));
        OrderDate = orderDate;
        ExpectedReceiptDate = expectedReceiptDate;
        ExternalReference = NormalizeOptionalUpper(externalReference, 100);
        SourceType = NormalizeRequired(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = NormalizeOptional(sourceReference, 200);
        CurrencyCodeSnapshot = NormalizeCurrency(currencyCodeSnapshot);
        Notes = NormalizeOptional(notes, 2_000);
        CreatedByUserId = NormalizeOptional(createdByUserId, 450);
    }

    public string DocumentNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public string WarehouseCodeSnapshot { get; private set; } = string.Empty;
    public int SupplierId { get; private set; }
    public string SupplierCodeSnapshot { get; private set; } = string.Empty;
    public string SupplierNameSnapshot { get; private set; } = string.Empty;
    public DateOnly OrderDate { get; private set; }
    public DateOnly? ExpectedReceiptDate { get; private set; }
    public string? ExternalReference { get; private set; }
    public string SourceType { get; private set; } = "MANUAL";
    public string? SourceReference { get; private set; }
    public string? CurrencyCodeSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public PurchaseOrderStatus Status { get; private set; } = PurchaseOrderStatus.Draft;
    public string? CreatedByUserId { get; private set; }
    public string? ConfirmedByUserId { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? ClosedByUserId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;
    public Supplier Supplier { get; private set; } = null!;
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public bool HasReceiptHistory => _lines.Any(line => line.HasReceiptHistory);
    public bool CanEdit => Status == PurchaseOrderStatus.Draft;
    public bool CanReceive => Status is PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived;

    public void UpdateDraft(
        DateOnly orderDate,
        DateOnly? expectedReceiptDate,
        string? externalReference,
        string sourceType,
        string? sourceReference,
        string? currencyCodeSnapshot,
        string? notes)
    {
        EnsureDraft();
        OrderDate = orderDate;
        ExpectedReceiptDate = expectedReceiptDate;
        ExternalReference = NormalizeOptionalUpper(externalReference, 100);
        SourceType = NormalizeRequired(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = NormalizeOptional(sourceReference, 200);
        CurrencyCodeSnapshot = NormalizeCurrency(currencyCodeSnapshot);
        Notes = NormalizeOptional(notes, 2_000);
        Revision++;
        SetUpdatedAt();
    }

    public void ReplaceDraftLines(IEnumerable<PurchaseOrderLine> lines)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(lines);
        if (_lines.Count > 0 && _lines.Any(line => line.HasReceiptHistory))
        {
            throw new InvalidOperationException("Purchase-order lines with receipt history cannot be replaced.");
        }

        var materialized = lines.ToArray();
        if (materialized.Length == 0)
        {
            throw new InvalidOperationException("A purchase order must contain at least one line.");
        }

        if (materialized.Select(line => line.LineNumber).Distinct().Count() != materialized.Length)
        {
            throw new InvalidOperationException("Purchase-order line numbers must be unique.");
        }

        _lines.Clear();
        _lines.AddRange(materialized.OrderBy(line => line.LineNumber));
        Revision++;
        SetUpdatedAt();
    }

    public void Confirm(string userId, DateTime confirmedAtUtc)
    {
        EnsureUser(userId);
        if (Status != PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException($"A purchase order in {Status} cannot be confirmed.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A purchase order must contain at least one line before confirmation.");
        }

        Status = PurchaseOrderStatus.Confirmed;
        ConfirmedByUserId = userId.Trim();
        ConfirmedAtUtc = NormalizeUtc(confirmedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void RecordReceipt(int lineId, decimal baseQuantity)
    {
        if (!CanReceive)
        {
            throw new InvalidOperationException($"A purchase order in {Status} cannot receive quantity.");
        }

        var line = _lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            throw new InvalidOperationException("The purchase-order line was not found.");
        }

        line.RecordReceipt(baseQuantity);
        Status = _lines.All(candidate => candidate.IsFullyReceived)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;
        Revision++;
        SetUpdatedAt();
    }

    public void Close(string userId, DateTime closedAtUtc)
    {
        EnsureUser(userId);
        if (Status is not (PurchaseOrderStatus.Confirmed or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received))
        {
            throw new InvalidOperationException($"A purchase order in {Status} cannot be closed.");
        }

        foreach (var line in _lines)
        {
            line.Close();
        }

        Status = PurchaseOrderStatus.Closed;
        ClosedByUserId = userId.Trim();
        ClosedAtUtc = NormalizeUtc(closedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void Cancel(string userId, DateTime cancelledAtUtc)
    {
        EnsureUser(userId);
        if (Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Confirmed))
        {
            throw new InvalidOperationException(
                $"A purchase order in {Status} cannot be cancelled after receipt history exists.");
        }

        Status = PurchaseOrderStatus.Cancelled;
        CancelledByUserId = userId.Trim();
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void Reopen()
    {
        if (Status == PurchaseOrderStatus.Cancelled)
        {
            Status = PurchaseOrderStatus.Draft;
            CancelledByUserId = null;
            CancelledAtUtc = null;
        }
        else if (Status == PurchaseOrderStatus.Closed)
        {
            foreach (var line in _lines)
            {
                line.Reopen();
            }

            Status = _lines.All(line => line.IsFullyReceived)
                ? PurchaseOrderStatus.Received
                : _lines.Any(line => line.HasReceiptHistory)
                    ? PurchaseOrderStatus.PartiallyReceived
                    : PurchaseOrderStatus.Confirmed;
            ClosedByUserId = null;
            ClosedAtUtc = null;
        }
        else
        {
            throw new InvalidOperationException($"A purchase order in {Status} cannot be reopened.");
        }

        Revision++;
        SetUpdatedAt();
    }

    private void EnsureDraft()
    {
        if (!CanEdit)
        {
            throw new InvalidOperationException("Only draft purchase orders can be edited.");
        }
    }

    private static void EnsureUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }
    }

    private static string NormalizeRequired(string value, int maximumLength, string parameterName)
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

    private static string? NormalizeOptional(string? value, int maximumLength)
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

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();

    private static string? NormalizeCurrency(string? value)
    {
        var normalized = NormalizeOptionalUpper(value, 3);
        return normalized is null || normalized.Length == 3
            ? normalized
            : throw new ArgumentException("Currency code must contain exactly three characters.");
    }
}
