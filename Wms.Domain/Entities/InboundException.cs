#pragma warning disable CA1711

using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Durable inbound exception ledger entry. The row is the current queue state;
/// immutable audit records capture every assignment, review, and resolution.
/// </summary>
public sealed class InboundException : Entity
{
    private InboundException()
    {
    }

    public InboundException(
        int warehouseId,
        string exceptionNumber,
        string idempotencyKey,
        InboundExceptionCode code,
        InboundExceptionSeverity severity,
        string reason,
        string? queueCode = null,
        DateTime? dueAtUtc = null,
        int? advanceShippingNoticeId = null,
        int? advanceShippingNoticeLineId = null,
        int? receiptId = null,
        int? receiptLineId = null,
        int? receivingSessionId = null,
        int? licensePlateId = null,
        int? warehouseWorkId = null,
        int? itemId = null,
        int? stagingLocationId = null,
        decimal? expectedBaseQuantity = null,
        decimal? actualBaseQuantity = null,
        decimal? varianceBaseQuantity = null,
        string? itemSkuSnapshot = null,
        string? notes = null,
        string? attachmentReferences = null,
        string? createdByUserId = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        ValidateOptionalId(advanceShippingNoticeId, nameof(advanceShippingNoticeId));
        ValidateOptionalId(advanceShippingNoticeLineId, nameof(advanceShippingNoticeLineId));
        ValidateOptionalId(receiptId, nameof(receiptId));
        ValidateOptionalId(receiptLineId, nameof(receiptLineId));
        ValidateOptionalId(receivingSessionId, nameof(receivingSessionId));
        ValidateOptionalId(licensePlateId, nameof(licensePlateId));
        ValidateOptionalId(warehouseWorkId, nameof(warehouseWorkId));
        ValidateOptionalId(itemId, nameof(itemId));
        ValidateOptionalId(stagingLocationId, nameof(stagingLocationId));

        WarehouseId = warehouseId;
        ExceptionNumber = Required(exceptionNumber, 80, nameof(exceptionNumber));
        IdempotencyKey = Required(idempotencyKey, 250, nameof(idempotencyKey));
        Code = code;
        Severity = severity;
        Reason = Required(reason, 2_000, nameof(reason));
        QueueCode = OptionalUpper(queueCode, 50) ?? "INBOUND-EXCEPTION";
        DueAtUtc = dueAtUtc.HasValue ? NormalizeUtc(dueAtUtc.Value) : null;
        AdvanceShippingNoticeId = advanceShippingNoticeId;
        AdvanceShippingNoticeLineId = advanceShippingNoticeLineId;
        ReceiptId = receiptId;
        ReceiptLineId = receiptLineId;
        ReceivingSessionId = receivingSessionId;
        LicensePlateId = licensePlateId;
        WarehouseWorkId = warehouseWorkId;
        ItemId = itemId;
        StagingLocationId = stagingLocationId;
        ExpectedBaseQuantity = ValidateQuantity(expectedBaseQuantity, nameof(expectedBaseQuantity));
        ActualBaseQuantity = ValidateQuantity(actualBaseQuantity, nameof(actualBaseQuantity));
        VarianceBaseQuantity = varianceBaseQuantity;
        ItemSkuSnapshot = OptionalUpper(itemSkuSnapshot, 50);
        Notes = Optional(notes, 2_000);
        AttachmentReferences = Optional(attachmentReferences, 4_000);
        CreatedByUserId = Required(createdByUserId ?? "system", 450, nameof(createdByUserId));
        Status = InboundExceptionStatus.Open;
        Revision = 1;
    }

    public int WarehouseId { get; private set; }
    public string ExceptionNumber { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public InboundExceptionCode Code { get; private set; }
    public InboundExceptionSeverity Severity { get; private set; }
    public InboundExceptionStatus Status { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string QueueCode { get; private set; } = string.Empty;
    public DateTime? DueAtUtc { get; private set; }
    public int? AdvanceShippingNoticeId { get; private set; }
    public int? AdvanceShippingNoticeLineId { get; private set; }
    public int? ReceiptId { get; private set; }
    public int? ReceiptLineId { get; private set; }
    public int? ReceivingSessionId { get; private set; }
    public int? LicensePlateId { get; private set; }
    public int? WarehouseWorkId { get; private set; }
    public int? ItemId { get; private set; }
    public int? StagingLocationId { get; private set; }
    public decimal? ExpectedBaseQuantity { get; private set; }
    public decimal? ActualBaseQuantity { get; private set; }
    public decimal? VarianceBaseQuantity { get; private set; }
    public string? ItemSkuSnapshot { get; private set; }
    public string? Notes { get; private set; }
    public string? AttachmentReferences { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public string? OwnerUserId { get; private set; }
    public string? AssignedTeamCode { get; private set; }
    public string? AssignedByUserId { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public string? ReviewStartedByUserId { get; private set; }
    public DateTime? ReviewStartedAtUtc { get; private set; }
    public InboundExceptionResolution? Resolution { get; private set; }
    public string? ResolutionReason { get; private set; }
    public string? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public Warehouse Warehouse { get; private set; } = null!;
    public AdvanceShippingNotice? AdvanceShippingNotice { get; private set; }
    public AdvanceShippingNoticeLine? AdvanceShippingNoticeLine { get; private set; }
    public Receipt? Receipt { get; private set; }
    public ReceiptLine? ReceiptLine { get; private set; }
    public ReceivingSession? ReceivingSession { get; private set; }
    public LicensePlate? LicensePlate { get; private set; }
    public WarehouseWork? WarehouseWork { get; private set; }
    public Item? Item { get; private set; }
    public Location? StagingLocation { get; private set; }

    public bool IsTerminal => Status is InboundExceptionStatus.Resolved or InboundExceptionStatus.Cancelled;
    public bool IsOverdue(DateTime atUtc) => !IsTerminal && DueAtUtc.HasValue && DueAtUtc.Value < NormalizeUtc(atUtc);

    public void Assign(string? ownerUserId, string? teamCode, string assignedByUserId, DateTime assignedAtUtc)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(ownerUserId) && string.IsNullOrWhiteSpace(teamCode))
        {
            throw new ArgumentException("An exception assignment requires a user or team.");
        }

        OwnerUserId = Optional(ownerUserId, 450);
        AssignedTeamCode = OptionalUpper(teamCode, 50);
        AssignedByUserId = Required(assignedByUserId, 450, nameof(assignedByUserId));
        AssignedAtUtc = NormalizeUtc(assignedAtUtc);
        Revision++;
        SetUpdatedAt(assignedAtUtc);
    }

    public void StartReview(string userId, DateTime startedAtUtc)
    {
        EnsureEditable();
        ReviewStartedByUserId = Required(userId, 450, nameof(userId));
        ReviewStartedAtUtc = NormalizeUtc(startedAtUtc);
        Status = InboundExceptionStatus.UnderReview;
        Revision++;
        SetUpdatedAt(startedAtUtc);
    }

    public void ApplyResolution(
        InboundExceptionResolution resolution,
        string reason,
        string userId,
        DateTime resolvedAtUtc)
    {
        EnsureEditable();
        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

        Resolution = resolution;
        ResolutionReason = Required(reason, 2_000, nameof(reason));
        ResolvedByUserId = Required(userId, 450, nameof(userId));
        ResolvedAtUtc = NormalizeUtc(resolvedAtUtc);
        Status = resolution switch
        {
            InboundExceptionResolution.Hold => InboundExceptionStatus.Held,
            InboundExceptionResolution.SupervisorReview => InboundExceptionStatus.UnderReview,
            InboundExceptionResolution.Cancel => InboundExceptionStatus.Cancelled,
            _ => InboundExceptionStatus.Resolved
        };
        Revision++;
        SetUpdatedAt(resolvedAtUtc);
    }

    public void Cancel(string reason, string userId, DateTime cancelledAtUtc) =>
        ApplyResolution(InboundExceptionResolution.Cancel, reason, userId, cancelledAtUtc);

    private void EnsureEditable()
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"An inbound exception in {Status} cannot be changed.");
        }
    }

    private static void ValidateOptionalId(int? value, string parameterName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static decimal? ValidateQuantity(decimal? value, string parameterName) =>
        value is null
            ? null
            : value.Value >= 0m
                ? value
                : throw new ArgumentOutOfRangeException(parameterName);

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

#pragma warning restore CA1711
