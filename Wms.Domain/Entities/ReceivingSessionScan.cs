using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed class ReceivingSessionScan : Entity
{
    private ReceivingSessionScan()
    {
    }

    public ReceivingSessionScan(
        ReceivingSession session,
        string clientOperationId,
        string rawScanValue,
        string itemSku,
        decimal enteredQuantity,
        string? unitOfMeasure,
        string? packagingCode,
        string? lotNumber,
        DateTime? expiryDate,
        string? serialNumber,
        int? licensePlateId,
        int? sessionLineId,
        string userId,
        DateTime requestedAtUtc,
        string? destinationLocationCode = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ClientOperationId = Required(clientOperationId, 100, nameof(clientOperationId));
        RawScanValue = Required(rawScanValue, 200, nameof(rawScanValue));
        ItemSku = Required(itemSku, 50, nameof(itemSku)).ToUpperInvariant();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(enteredQuantity, 0m);

        Session = session;
        UnitOfMeasure = OptionalUpper(unitOfMeasure, 20);
        PackagingCode = OptionalUpper(packagingCode, 40);
        LotNumber = OptionalUpper(lotNumber, 100);
        ExpiryDate = expiryDate.HasValue ? NormalizeUtc(expiryDate.Value) : null;
        SerialNumber = OptionalUpper(serialNumber, 100);
        LicensePlateId = licensePlateId;
        ReceivingSessionLineId = sessionLineId;
        EnteredQuantity = enteredQuantity;
        DestinationLocationCode = OptionalUpper(destinationLocationCode, 50);
        Notes = Optional(notes, 1_000);
        RequestedByUserId = Required(userId, 450, nameof(userId));
        RequestedAtUtc = NormalizeUtc(requestedAtUtc);
        Status = ReceivingScanStatus.Pending;
    }

    public int ReceivingSessionId { get; private set; }
    public int? ReceivingSessionLineId { get; private set; }
    public string ClientOperationId { get; private set; } = string.Empty;
    public string RawScanValue { get; private set; } = string.Empty;
    public string ItemSku { get; private set; } = string.Empty;
    public decimal EnteredQuantity { get; private set; }
    public decimal? BaseQuantity { get; private set; }
    public string? UnitOfMeasure { get; private set; }
    public string? PackagingCode { get; private set; }
    public string? LotNumber { get; private set; }
    public DateTime? ExpiryDate { get; private set; }
    public string? SerialNumber { get; private set; }
    public int? LicensePlateId { get; private set; }
    public string? DestinationLocationCode { get; private set; }
    public string? Notes { get; private set; }
    public string RequestedByUserId { get; private set; } = string.Empty;
    public DateTime RequestedAtUtc { get; private set; }
    public ReceivingScanStatus Status { get; private set; }
    public int? MovementId { get; private set; }
    public int? ReceiptId { get; private set; }
    public string? ReceiptDocumentNumber { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime? CorrectedAtUtc { get; private set; }
    public string? CorrectionReason { get; private set; }
    public long Revision { get; private set; } = 1;

    public ReceivingSession Session { get; private set; } = null!;
    public ReceivingSessionLine? SessionLine { get; private set; }
    public Movement? Movement { get; private set; }
    public Receipt? Receipt { get; private set; }

    public bool IsReplayable => Status is ReceivingScanStatus.Pending or ReceivingScanStatus.Failed;

    public void SetIdempotencyKey(string key)
    {
        IdempotencyKey = Required(key, 250, nameof(key));
        Revision++;
    }

    public void MarkCompleted(
        decimal baseQuantity,
        int movementId,
        int? receiptId,
        string? receiptDocumentNumber,
        DateTime completedAtUtc)
    {
        if (baseQuantity <= 0m || movementId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseQuantity));
        }

        BaseQuantity = baseQuantity;
        MovementId = movementId;
        ReceiptId = receiptId;
        ReceiptDocumentNumber = Optional(receiptDocumentNumber, 50);
        Status = ReceivingScanStatus.Completed;
        ErrorCode = null;
        ErrorMessage = null;
        CompletedAtUtc = NormalizeUtc(completedAtUtc);
        Revision++;
        SetUpdatedAt(completedAtUtc);
    }

    public void MarkFailed(string errorCode, string errorMessage, DateTime failedAtUtc)
    {
        ErrorCode = Required(errorCode, 200, nameof(errorCode));
        ErrorMessage = Required(errorMessage, 2_000, nameof(errorMessage));
        Status = ReceivingScanStatus.Failed;
        Revision++;
        SetUpdatedAt(failedAtUtc);
    }

    public void MarkCorrected(string reason, DateTime correctedAtUtc)
    {
        if (Status != ReceivingScanStatus.Completed)
        {
            throw new InvalidOperationException("Only a completed scan can be corrected.");
        }

        CorrectionReason = Required(reason, 1_000, nameof(reason));
        Status = ReceivingScanStatus.Corrected;
        CorrectedAtUtc = NormalizeUtc(correctedAtUtc);
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

    private static string? Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, maximumLength, nameof(value));

    private static string? OptionalUpper(string? value, int maximumLength) =>
        Optional(value, maximumLength)?.ToUpperInvariant();

}
