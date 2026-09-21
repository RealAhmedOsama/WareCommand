using Wms.Application.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Attachments;

public static class AttachmentReferenceTypes
{
    public const string Receipt = "receipt";
    public const string Damage = "damage";
    public const string QualityInspection = "quality-inspection";
    public const string InboundException = "inbound-exception";
    public const string OutboundException = "outbound-exception";
    public const string Return = "return";
    public const string Approval = "approval";
    public const string Shipment = "shipment";
    public const string AuditEvidence = "audit-evidence";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
    [
        Receipt,
        Damage,
        QualityInspection,
        InboundException,
        OutboundException,
        Return,
        Approval,
        Shipment,
        AuditEvidence
    ],
        StringComparer.Ordinal);

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return All.Contains(normalized);
    }
}

public enum AttachmentAccessMode
{
    Read = 1,
    Manage = 2
}

public sealed record AttachmentReferenceAccessRequest(
    string ReferenceType,
    string ReferenceId,
    int WarehouseId,
    AttachmentAccessMode Mode);

public interface IAttachmentReferenceAccessService
{
    Task<Result> AuthorizeAsync(
        AttachmentReferenceAccessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AttachmentUploadInput(
    string ReferenceType,
    string ReferenceId,
    int WarehouseId,
    string FileName,
    string ContentType,
    long DeclaredLength,
    AttachmentClassification Classification = AttachmentClassification.Operational,
    DateTimeOffset? RetentionUntilUtc = null,
    bool ImmutableEvidence = false);

public sealed record AttachmentQuery(
    string ReferenceType,
    string ReferenceId,
    int WarehouseId);

public sealed record AttachmentDto(
    int Id,
    string ReferenceType,
    string ReferenceId,
    int WarehouseId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string UploaderUserId,
    AttachmentClassification Classification,
    AttachmentScanStatus ScanStatus,
    AttachmentRetentionState RetentionState,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? RetentionUntilUtc,
    bool IsImmutableEvidence,
    bool IsDownloadable,
    bool IsPreviewAvailable,
    string? LastScanMessage,
    string? DeletedByUserId,
    DateTimeOffset? DeletedAtUtc);

public sealed record AttachmentDownload(
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    bool IsPreview);

public sealed record AttachmentStorageWrite(
    string StorageKey,
    Stream Content,
    long Length,
    string ContentType);

public interface IAttachmentStorage
{
    Task StoreAsync(
        AttachmentStorageWrite write,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}

public sealed record AttachmentScanRequest(
    string StorageKey,
    string ContentType,
    long SizeBytes,
    string Sha256);

public sealed record AttachmentScanResult(
    AttachmentScanStatus Status,
    string? Message = null);

public interface IAttachmentScanner
{
    bool IsConfigured { get; }

    Task<AttachmentScanResult> ScanAsync(
        AttachmentScanRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAttachmentService
{
    Task<Result<AttachmentDto>> UploadAsync(
        AttachmentUploadInput input,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(
        AttachmentQuery query,
        CancellationToken cancellationToken = default);

    Task<Result<AttachmentDownload>> OpenDownloadAsync(
        int attachmentId,
        bool preview,
        CancellationToken cancellationToken = default);

    Task<Result<AttachmentDto>> MarkEvidenceAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);

    Task<Result<AttachmentDto>> RequestDeletionAsync(
        int attachmentId,
        CancellationToken cancellationToken = default);
}
