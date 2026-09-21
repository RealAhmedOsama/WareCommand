using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

/// <summary>
/// Metadata for a warehouse attachment. The file bytes are owned by a storage
/// adapter; this entity deliberately contains no blob column or client path.
/// </summary>
public sealed class Attachment : Entity
{
    private Attachment()
    {
    }

    public Attachment(
        string referenceType,
        string referenceId,
        int warehouseId,
        string fileName,
        string storageKey,
        string contentType,
        long sizeBytes,
        string sha256,
        string uploaderUserId,
        AttachmentClassification classification,
        AttachmentScanStatus scanStatus,
        DateTimeOffset uploadedAtUtc,
        DateTimeOffset? retentionUntilUtc,
        bool immutableEvidence)
    {
        ReferenceType = Required(referenceType, 80, nameof(referenceType));
        ReferenceId = Required(referenceId, 200, nameof(referenceId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        WarehouseId = warehouseId;
        FileName = Required(fileName, 180, nameof(fileName));
        StorageKey = Required(storageKey, 500, nameof(storageKey));
        ContentType = Required(contentType, 120, nameof(contentType));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        Sha256 = Required(sha256, 64, nameof(sha256));
        if (Sha256.Length != 64 || Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("The attachment hash must be a SHA-256 hex value.", nameof(sha256));
        }

        UploaderUserId = Required(uploaderUserId, 450, nameof(uploaderUserId));
        if (!Enum.IsDefined(classification))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        if (!Enum.IsDefined(scanStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(scanStatus));
        }

        Classification = classification;
        ScanStatus = scanStatus;
        UploadedAtUtc = uploadedAtUtc.ToUniversalTime();
        RetentionUntilUtc = retentionUntilUtc?.ToUniversalTime();
        IsImmutableEvidence = immutableEvidence;
        RetentionState = scanStatus is AttachmentScanStatus.Suspicious or AttachmentScanStatus.Failed
            ? AttachmentRetentionState.Quarantined
            : immutableEvidence
                ? AttachmentRetentionState.Retained
                : AttachmentRetentionState.Active;
    }

    public string ReferenceType { get; private set; } = string.Empty;
    public string ReferenceId { get; private set; } = string.Empty;
    public int WarehouseId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public string UploaderUserId { get; private set; } = string.Empty;
    public AttachmentClassification Classification { get; private set; }
    public AttachmentScanStatus ScanStatus { get; private set; }
    public AttachmentRetentionState RetentionState { get; private set; }
    public DateTimeOffset UploadedAtUtc { get; private set; }
    public DateTimeOffset? RetentionUntilUtc { get; private set; }
    public bool IsImmutableEvidence { get; private set; }
    public string? LastScanMessage { get; private set; }
    public string? DeletedByUserId { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool IsDownloadable =>
        RetentionState is AttachmentRetentionState.Active or AttachmentRetentionState.Retained &&
        ScanStatus is AttachmentScanStatus.NotConfigured or AttachmentScanStatus.Clean;

    public void ApplyScanResult(
        AttachmentScanStatus scanStatus,
        string? scanMessage,
        DateTimeOffset scannedAtUtc)
    {
        if (RetentionState is AttachmentRetentionState.Deleted or AttachmentRetentionState.PendingDeletion)
        {
            throw new InvalidOperationException("A deleted attachment cannot be scanned.");
        }

        if (!Enum.IsDefined(scanStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(scanStatus));
        }

        ScanStatus = scanStatus;
        LastScanMessage = Optional(scanMessage, 1_000);
        RetentionState = scanStatus is AttachmentScanStatus.Suspicious or AttachmentScanStatus.Failed
            ? AttachmentRetentionState.Quarantined
            : IsImmutableEvidence
                ? AttachmentRetentionState.Retained
                : AttachmentRetentionState.Active;
        SetUpdatedAt(scannedAtUtc.UtcDateTime);
    }

    public void MarkImmutableEvidence(DateTimeOffset markedAtUtc)
    {
        EnsureAvailableForMutation();
        IsImmutableEvidence = true;
        RetentionState = AttachmentRetentionState.Retained;
        SetUpdatedAt(markedAtUtc.UtcDateTime);
    }

    public void RequestDeletion(string userId, DateTimeOffset requestedAtUtc)
    {
        EnsureAvailableForMutation();
        if (IsImmutableEvidence)
        {
            throw new InvalidOperationException(
                "Immutable evidence cannot be deleted or replaced.");
        }

        if (RetentionUntilUtc.HasValue && RetentionUntilUtc.Value > requestedAtUtc.ToUniversalTime())
        {
            throw new InvalidOperationException(
                "The attachment is protected by its retention period.");
        }

        DeletedByUserId = Required(userId, 450, nameof(userId));
        RetentionState = AttachmentRetentionState.PendingDeletion;
        SetUpdatedAt(requestedAtUtc.UtcDateTime);
    }

    public void MarkDeleted(DateTimeOffset deletedAtUtc)
    {
        if (RetentionState != AttachmentRetentionState.PendingDeletion)
        {
            throw new InvalidOperationException(
                "Only an attachment pending deletion can be marked deleted.");
        }

        DeletedAtUtc = deletedAtUtc.ToUniversalTime();
        RetentionState = AttachmentRetentionState.Deleted;
        SetUpdatedAt(deletedAtUtc.UtcDateTime);
    }

    private void EnsureAvailableForMutation()
    {
        if (RetentionState is AttachmentRetentionState.Deleted or AttachmentRetentionState.PendingDeletion)
        {
            throw new InvalidOperationException("The attachment is no longer mutable.");
        }
    }

    private static string Required(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string? Optional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
