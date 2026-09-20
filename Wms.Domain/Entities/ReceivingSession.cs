using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Durable operator work context for scanner-first inbound execution. A session
/// is deliberately separate from a receipt document: one session may produce
/// many receipt documents while preserving one retryable operator ledger.
/// </summary>
public sealed class ReceivingSession : Entity
{
    private readonly List<ReceivingSessionLine> _lines = [];
    private readonly List<ReceivingSessionScan> _scans = [];

    private ReceivingSession()
    {
    }

    public ReceivingSession(
        int warehouseId,
        int receivingLocationId,
        ReceivingSessionSourceType sourceType,
        string sessionReference,
        string userId,
        DateTime openedAtUtc,
        int? purchaseOrderId = null,
        int? advanceShippingNoticeId = null,
        int? dockLocationId = null,
        string? externalReference = null,
        string? notes = null,
        bool supervisorOverride = false,
        string? supervisorOverrideReason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(receivingLocationId);
        SessionReference = Required(sessionReference, 100, nameof(sessionReference));
        CreatedByUserId = Required(userId, 450, nameof(userId));
        if (purchaseOrderId is <= 0 || advanceShippingNoticeId is <= 0 || dockLocationId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(purchaseOrderId),
                "Optional identifiers must be positive when supplied.");
        }

        if (sourceType == ReceivingSessionSourceType.PurchaseOrder && !purchaseOrderId.HasValue)
        {
            throw new ArgumentException("A purchase-order receiving session requires a purchase order.", nameof(purchaseOrderId));
        }

        if (sourceType == ReceivingSessionSourceType.AdvanceShippingNotice && !advanceShippingNoticeId.HasValue)
        {
            throw new ArgumentException("An ASN receiving session requires an advance shipping notice.", nameof(advanceShippingNoticeId));
        }

        if (sourceType == ReceivingSessionSourceType.BlindReceipt && !supervisorOverride)
        {
            throw new ArgumentException("A blind receiving session requires a supervisor override.", nameof(supervisorOverride));
        }

        if (supervisorOverride && string.IsNullOrWhiteSpace(supervisorOverrideReason))
        {
            throw new ArgumentException("A supervisor override reason is required.", nameof(supervisorOverrideReason));
        }

        WarehouseId = warehouseId;
        ReceivingLocationId = receivingLocationId;
        SourceType = sourceType;
        SessionReference = SessionReference.ToUpperInvariant();
        PurchaseOrderId = purchaseOrderId;
        AdvanceShippingNoticeId = advanceShippingNoticeId;
        DockLocationId = dockLocationId;
        ExternalReference = OptionalUpper(externalReference, 100);
        Notes = Optional(notes, 2_000);
        SupervisorOverride = supervisorOverride;
        SupervisorOverrideReason = Optional(supervisorOverrideReason, 1_000);
        Status = ReceivingSessionStatus.Open;
        OpenedAtUtc = NormalizeUtc(openedAtUtc);
        LastActivityAtUtc = OpenedAtUtc;
    }

    public int WarehouseId { get; private set; }
    public int ReceivingLocationId { get; private set; }
    public ReceivingSessionSourceType SourceType { get; private set; }
    public string SessionReference { get; private set; } = string.Empty;
    public int? PurchaseOrderId { get; private set; }
    public int? AdvanceShippingNoticeId { get; private set; }
    public int? DockLocationId { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? Notes { get; private set; }
    public ReceivingSessionStatus Status { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public bool SupervisorOverride { get; private set; }
    public string? SupervisorOverrideReason { get; private set; }
    public DateTime OpenedAtUtc { get; private set; }
    public DateTime? PausedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public long Revision { get; private set; } = 1;

    public Warehouse Warehouse { get; private set; } = null!;
    public Location ReceivingLocation { get; private set; } = null!;
    public PurchaseOrder? PurchaseOrder { get; private set; }
    public AdvanceShippingNotice? AdvanceShippingNotice { get; private set; }
    public Location? DockLocation { get; private set; }
    public IReadOnlyList<ReceivingSessionLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<ReceivingSessionScan> Scans => _scans.AsReadOnly();

    public bool CanScan => Status == ReceivingSessionStatus.Open;
    public bool HasScans => _scans.Any(scan => scan.Status is ReceivingScanStatus.Completed or ReceivingScanStatus.Corrected);
    public bool HasOpenDemand => _lines.Any(line => line.RemainingBaseQuantity is > 0m);

    public void AddLine(ReceivingSessionLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Status != ReceivingSessionStatus.Open)
        {
            throw new InvalidOperationException("Demand lines can only be added to an open receiving session.");
        }

        if (_lines.Any(existing => existing.LineNumber == line.LineNumber))
        {
            throw new InvalidOperationException("Receiving session line numbers must be unique.");
        }

        _lines.Add(line);
        // The line is a newly constructed snapshot and has no persistence
        // timestamp yet. Preserve the session clock here; the execution
        // boundary stamps the actual scan/start activity explicitly.
        Touch(LastActivityAtUtc);
    }

    public void AddScan(ReceivingSessionScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (!CanScan)
        {
            throw new InvalidOperationException($"A receiving session in {Status} cannot accept scans.");
        }

        if (_scans.Any(existing => string.Equals(
                existing.ClientOperationId,
                scan.ClientOperationId,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("The client operation has already been recorded for this session.");
        }

        _scans.Add(scan);
        Touch(scan.RequestedAtUtc);
    }

    public void Pause(DateTime pausedAtUtc)
    {
        if (Status != ReceivingSessionStatus.Open)
        {
            throw new InvalidOperationException($"A receiving session in {Status} cannot be paused.");
        }

        Status = ReceivingSessionStatus.Paused;
        PausedAtUtc = NormalizeUtc(pausedAtUtc);
        Touch(pausedAtUtc);
    }

    public void Resume(DateTime resumedAtUtc)
    {
        if (Status != ReceivingSessionStatus.Paused)
        {
            throw new InvalidOperationException($"A receiving session in {Status} cannot be resumed.");
        }

        Status = ReceivingSessionStatus.Open;
        PausedAtUtc = null;
        Touch(resumedAtUtc);
    }

    public void Complete(bool allowPartial, DateTime completedAtUtc)
    {
        if (Status is not (ReceivingSessionStatus.Open or ReceivingSessionStatus.Paused))
        {
            throw new InvalidOperationException($"A receiving session in {Status} cannot be completed.");
        }

        if (!HasScans)
        {
            throw new InvalidOperationException("A receiving session cannot be completed before a scan succeeds.");
        }

        if (!allowPartial && HasOpenDemand)
        {
            throw new InvalidOperationException(
                "The receiving session still has open demand. Complete it partially with an authorized reason or receive the remaining quantity.");
        }

        Status = HasOpenDemand ? ReceivingSessionStatus.PartiallyCompleted : ReceivingSessionStatus.Completed;
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Touch(completedAtUtc);
    }

    public void Cancel(string userId, string reason, DateTime cancelledAtUtc)
    {
        if (Status is ReceivingSessionStatus.Completed or ReceivingSessionStatus.PartiallyCompleted or ReceivingSessionStatus.Cancelled)
        {
            throw new InvalidOperationException($"A receiving session in {Status} cannot be cancelled.");
        }

        CancelledByUserId = Required(userId, 450, nameof(userId));
        CancellationReason = Required(reason, 1_000, nameof(reason));
        Status = ReceivingSessionStatus.Cancelled;
        CancelledAtUtc = NormalizeUtc(cancelledAtUtc);
        Touch(cancelledAtUtc);
    }

    public void Touch(DateTime occurredAtUtc)
    {
        LastActivityAtUtc = NormalizeUtc(occurredAtUtc);
        Revision++;
        SetUpdatedAt(occurredAtUtc);
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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value));

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();

}
