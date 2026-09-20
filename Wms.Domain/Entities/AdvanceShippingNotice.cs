using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Supplier shipment expectation. Arrival and dock check-in are document-only
/// state changes; inventory changes only when a receiving movement allocates to
/// an ASN line.
/// </summary>
public sealed class AdvanceShippingNotice : Entity
{
    private readonly List<AdvanceShippingNoticeLine> _lines = new();

    private AdvanceShippingNotice()
    {
    }

    public AdvanceShippingNotice(
        string documentNumber,
        int warehouseId,
        string warehouseCodeSnapshot,
        int supplierId,
        string supplierCodeSnapshot,
        string supplierNameSnapshot,
        string? carrierName = null,
        DateTime? expectedArrivalFromUtc = null,
        DateTime? expectedArrivalToUtc = null,
        string? vehicleNumber = null,
        string? trailerNumber = null,
        string? containerNumber = null,
        string? trackingReference = null,
        string? externalReference = null,
        string sourceType = "MANUAL",
        string? sourceReference = null,
        string? sourcePayload = null,
        int? dockLocationId = null,
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
        CarrierName = NormalizeOptional(carrierName, 200);
        ExpectedArrivalFromUtc = NormalizeUtc(expectedArrivalFromUtc);
        ExpectedArrivalToUtc = NormalizeUtc(expectedArrivalToUtc);
        ValidateArrivalWindow(ExpectedArrivalFromUtc, ExpectedArrivalToUtc);
        VehicleNumber = NormalizeOptional(vehicleNumber, 100);
        TrailerNumber = NormalizeOptional(trailerNumber, 100);
        ContainerNumber = NormalizeOptional(containerNumber, 100);
        TrackingReference = NormalizeOptionalUpper(trackingReference, 100);
        ExternalReference = NormalizeOptionalUpper(externalReference, 100);
        SourceType = NormalizeRequired(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = NormalizeOptional(sourceReference, 200);
        SourcePayload = NormalizeOptional(sourcePayload, 8_000);
        DockLocationId = ValidateOptionalId(dockLocationId, nameof(dockLocationId));
        Notes = NormalizeOptional(notes, 2_000);
        CreatedByUserId = NormalizeOptional(createdByUserId, 450);
    }

    public string DocumentNumber { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public string WarehouseCodeSnapshot { get; private set; } = string.Empty;
    public int SupplierId { get; private set; }
    public string SupplierCodeSnapshot { get; private set; } = string.Empty;
    public string SupplierNameSnapshot { get; private set; } = string.Empty;
    public string? CarrierName { get; private set; }
    public DateTime? ExpectedArrivalFromUtc { get; private set; }
    public DateTime? ExpectedArrivalToUtc { get; private set; }
    public string? VehicleNumber { get; private set; }
    public string? TrailerNumber { get; private set; }
    public string? ContainerNumber { get; private set; }
    public string? TrackingReference { get; private set; }
    public string? ExternalReference { get; private set; }
    public string SourceType { get; private set; } = "MANUAL";
    public string? SourceReference { get; private set; }
    public string? SourcePayload { get; private set; }
    public int? DockLocationId { get; private set; }
    public string? Notes { get; private set; }
    public AdvanceShippingNoticeStatus Status { get; private set; } = AdvanceShippingNoticeStatus.Draft;
    public string? CreatedByUserId { get; private set; }
    public string? SubmittedByUserId { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ArrivedByUserId { get; private set; }
    public DateTime? ArrivedAtUtc { get; private set; }
    public string? CompletedByUserId { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;
    public Supplier Supplier { get; private set; } = null!;
    public Location? DockLocation { get; private set; }
    public IReadOnlyList<AdvanceShippingNoticeLine> Lines => _lines.AsReadOnly();

    public bool CanEdit => Status == AdvanceShippingNoticeStatus.Draft;
    public bool CanSubmit => Status == AdvanceShippingNoticeStatus.Draft;
    public bool CanArrive => Status is AdvanceShippingNoticeStatus.Submitted or AdvanceShippingNoticeStatus.Expected;
    public bool CanReceive => Status is AdvanceShippingNoticeStatus.Arrived or AdvanceShippingNoticeStatus.Receiving or AdvanceShippingNoticeStatus.Exception;
    public bool HasReceiptHistory => _lines.Any(line => line.HasReceiptHistory);
    public bool CanCancel => !HasReceiptHistory && Status is not (AdvanceShippingNoticeStatus.Completed or AdvanceShippingNoticeStatus.Cancelled or AdvanceShippingNoticeStatus.Receiving);

    public void UpdateDraft(
        string? carrierName,
        DateTime? expectedArrivalFromUtc,
        DateTime? expectedArrivalToUtc,
        string? vehicleNumber,
        string? trailerNumber,
        string? containerNumber,
        string? trackingReference,
        string? externalReference,
        string sourceType,
        string? sourceReference,
        string? sourcePayload,
        int? dockLocationId,
        string? notes)
    {
        EnsureDraft();
        CarrierName = NormalizeOptional(carrierName, 200);
        ExpectedArrivalFromUtc = NormalizeUtc(expectedArrivalFromUtc);
        ExpectedArrivalToUtc = NormalizeUtc(expectedArrivalToUtc);
        ValidateArrivalWindow(ExpectedArrivalFromUtc, ExpectedArrivalToUtc);
        VehicleNumber = NormalizeOptional(vehicleNumber, 100);
        TrailerNumber = NormalizeOptional(trailerNumber, 100);
        ContainerNumber = NormalizeOptional(containerNumber, 100);
        TrackingReference = NormalizeOptionalUpper(trackingReference, 100);
        ExternalReference = NormalizeOptionalUpper(externalReference, 100);
        SourceType = NormalizeRequired(sourceType, 30, nameof(sourceType)).ToUpperInvariant();
        SourceReference = NormalizeOptional(sourceReference, 200);
        SourcePayload = NormalizeOptional(sourcePayload, 8_000);
        DockLocationId = ValidateOptionalId(dockLocationId, nameof(dockLocationId));
        Notes = NormalizeOptional(notes, 2_000);
        Revision++;
        SetUpdatedAt();
    }

    public void ReplaceDraftLines(IEnumerable<AdvanceShippingNoticeLine> lines)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(lines);
        if (_lines.Any(line => line.HasReceiptHistory))
        {
            throw new InvalidOperationException("ASN lines with receipt history cannot be replaced.");
        }

        var materialized = lines.ToArray();
        if (materialized.Length == 0)
        {
            throw new InvalidOperationException("An advance shipping notice must contain at least one line.");
        }

        if (materialized.Select(line => line.LineNumber).Distinct().Count() != materialized.Length)
        {
            throw new InvalidOperationException("ASN line numbers must be unique.");
        }

        _lines.Clear();
        _lines.AddRange(materialized.OrderBy(line => line.LineNumber));
        Revision++;
        SetUpdatedAt();
    }

    public void Submit(string userId, DateTime submittedAtUtc)
    {
        EnsureUser(userId);
        if (Status != AdvanceShippingNoticeStatus.Draft)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be submitted.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("An ASN must contain at least one line before submission.");
        }

        Status = AdvanceShippingNoticeStatus.Submitted;
        SubmittedByUserId = userId.Trim();
        SubmittedAtUtc = NormalizeUtc(submittedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void MarkExpected(DateTime expectedAtUtc)
    {
        if (Status != AdvanceShippingNoticeStatus.Submitted)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be marked expected.");
        }

        Status = AdvanceShippingNoticeStatus.Expected;
        Revision++;
        SetUpdatedAt(expectedAtUtc);
    }

    public void AssignDock(int? dockLocationId)
    {
        if (Status is AdvanceShippingNoticeStatus.Completed or AdvanceShippingNoticeStatus.Cancelled)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be assigned to a dock.");
        }

        DockLocationId = ValidateOptionalId(dockLocationId, nameof(dockLocationId));
        Revision++;
        SetUpdatedAt();
    }

    public void Arrive(string userId, DateTime arrivedAtUtc, int? dockLocationId = null)
    {
        EnsureUser(userId);
        if (!CanArrive)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be checked in as arrived.");
        }

        if (dockLocationId.HasValue)
        {
            DockLocationId = ValidateOptionalId(dockLocationId, nameof(dockLocationId));
        }

        Status = AdvanceShippingNoticeStatus.Arrived;
        ArrivedByUserId = userId.Trim();
        ArrivedAtUtc = NormalizeUtc(arrivedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void RecordReceipt(int lineId, decimal baseQuantity, DateTime receivedAtUtc)
    {
        if (!CanReceive)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot receive quantity.");
        }

        var line = _lines.SingleOrDefault(candidate => candidate.Id == lineId);
        if (line is null)
        {
            throw new InvalidOperationException("The ASN line was not found.");
        }

        line.RecordReceipt(baseQuantity);
        Status = AdvanceShippingNoticeStatus.Receiving;
        Revision++;
        SetUpdatedAt(receivedAtUtc);
    }

    public void MarkException()
    {
        if (Status is not (AdvanceShippingNoticeStatus.Arrived or AdvanceShippingNoticeStatus.Receiving))
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be marked as an exception after check-in.");
        }

        Status = AdvanceShippingNoticeStatus.Exception;
        Revision++;
        SetUpdatedAt();
    }

    public void Complete(string userId, DateTime completedAtUtc)
    {
        EnsureUser(userId);
        if (Status is not (AdvanceShippingNoticeStatus.Receiving or AdvanceShippingNoticeStatus.Exception))
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be completed.");
        }

        foreach (var line in _lines)
        {
            line.Close();
        }

        Status = AdvanceShippingNoticeStatus.Completed;
        CompletedByUserId = userId.Trim();
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    public void Cancel(string userId, DateTime cancelledAtUtc)
    {
        EnsureUser(userId);
        if (!CanCancel)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be cancelled after receipt history exists.");
        }

        Status = AdvanceShippingNoticeStatus.Cancelled;
        CancelledByUserId = userId.Trim();
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Revision++;
        SetUpdatedAt();
    }

    private void EnsureDraft()
    {
        if (Status != AdvanceShippingNoticeStatus.Draft)
        {
            throw new InvalidOperationException($"An ASN in {Status} cannot be edited.");
        }
    }

    private static void EnsureUser(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }
    }

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static void ValidateArrivalWindow(DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue && toUtc.HasValue && toUtc < fromUtc)
        {
            throw new ArgumentException("Expected arrival end cannot be before expected arrival start.");
        }
    }

    private static int? ValidateOptionalId(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return value;
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

    private static string? NormalizeOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequired(value, maximumLength, nameof(value));

    private static string? NormalizeOptionalUpper(string? value, int maximumLength) =>
        NormalizeOptional(value, maximumLength)?.ToUpperInvariant();
}
