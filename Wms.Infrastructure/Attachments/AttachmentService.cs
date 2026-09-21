using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Attachments;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Attachments;

public sealed class AttachmentService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAttachmentReferenceAccessService referenceAccessService,
    IAttachmentStorage storage,
    IAttachmentScanner scanner,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    IClock clock,
    AttachmentStorageOptions options,
    ILogger<AttachmentService> logger) : IAttachmentService
{
    public async Task<Result<AttachmentDto>> UploadAsync(
        AttachmentUploadInput input,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(content);

        var actor = GetActor();
        if (actor.IsFailure)
        {
            return actor.ToFailure<AttachmentDto>();
        }

        if (!AttachmentReferenceTypes.TryNormalize(input.ReferenceType, out var referenceType) ||
            string.IsNullOrWhiteSpace(input.ReferenceId) ||
            input.WarehouseId <= 0 ||
            !Enum.IsDefined(input.Classification))
        {
            return Result.Failure<AttachmentDto>(WmsErrors.Validation(
                "attachments.input_invalid",
                "The attachment reference, warehouse, or classification is invalid."));
        }

        var referenceId = input.ReferenceId.Trim();
        if (referenceId.Length > 200 ||
            referenceId.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            return Result.Failure<AttachmentDto>(WmsErrors.Validation(
                "attachments.reference_invalid",
                "The attachment reference is invalid."));
        }

        var baseAuthorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AttachmentsManage,
            input.WarehouseId,
            cancellationToken);
        if (baseAuthorization.IsFailure)
        {
            return baseAuthorization.ToFailure<AttachmentDto>();
        }

        var referenceAuthorization = await referenceAccessService.AuthorizeAsync(
            new AttachmentReferenceAccessRequest(
                referenceType,
                referenceId,
                input.WarehouseId,
                AttachmentAccessMode.Manage),
            cancellationToken);
        if (referenceAuthorization.IsFailure)
        {
            return referenceAuthorization.ToFailure<AttachmentDto>();
        }

        if (input.RetentionUntilUtc.HasValue &&
            input.RetentionUntilUtc.Value <= clock.UtcNow)
        {
            return Result.Failure<AttachmentDto>(WmsErrors.Validation(
                "attachments.retention_invalid",
                "The retention date must be in the future."));
        }

        if (options.RequireAntivirusScan && !scanner.IsConfigured)
        {
            return Result.Failure<AttachmentDto>(WmsErrors.Dependency(
                "attachments.scanner_not_configured",
                "An antivirus scanner must be configured before uploads are accepted.",
                isRetryable: false));
        }

        var prepared = await PrepareContentAsync(input, content, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.ToFailure<AttachmentDto>();
        }

        var duplicate = await context.Attachments.AnyAsync(
            attachment => attachment.ReferenceType == referenceType &&
                          attachment.ReferenceId == referenceId &&
                          attachment.WarehouseId == input.WarehouseId &&
                          attachment.Sha256 == prepared.Value.Sha256,
            cancellationToken);
        if (duplicate)
        {
            TryDelete(prepared.Value.TempPath);
            return Result.Failure<AttachmentDto>(WmsErrors.Conflict(
                "attachments.duplicate",
                "The same file is already attached to this warehouse record."));
        }

        var attachmentCount = await context.Attachments.CountAsync(
            attachment => attachment.ReferenceType == referenceType &&
                          attachment.ReferenceId == referenceId &&
                          attachment.WarehouseId == input.WarehouseId &&
                          attachment.RetentionState != AttachmentRetentionState.Deleted,
            cancellationToken);
        if (attachmentCount >= options.MaximumAttachmentsPerReference)
        {
            TryDelete(prepared.Value.TempPath);
            return Result.Failure<AttachmentDto>(WmsErrors.Conflict(
                "attachments.count_limit",
                "The attachment count limit for this record has been reached."));
        }

        try
        {
            var storageKey = string.Concat(
                input.WarehouseId.ToString(CultureInfo.InvariantCulture),
                "/",
                referenceType,
                "/",
                Guid.NewGuid().ToString("N"));
            AttachmentScanResult scanResult;
            try
            {
                await using (var stagedContent = new FileStream(
                    prepared.Value.TempPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await storage.StoreAsync(
                        new AttachmentStorageWrite(
                            storageKey,
                            stagedContent,
                            prepared.Value.Length,
                        prepared.Value.ContentType),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Attachment storage failed for {ReferenceType}/{ReferenceId}",
                    referenceType,
                    referenceId);
                await TryDeleteStorageAsync(storageKey, cancellationToken);
                return Result.Failure<AttachmentDto>(WmsErrors.Dependency(
                    "attachments.storage_unavailable",
                    "The attachment storage service could not complete the upload."));
            }

            try
            {
                scanResult = scanner.IsConfigured
                    ? await scanner.ScanAsync(
                        new AttachmentScanRequest(
                            storageKey,
                            prepared.Value.ContentType,
                            prepared.Value.Length,
                            prepared.Value.Sha256),
                        cancellationToken)
                    : new AttachmentScanResult(AttachmentScanStatus.NotConfigured);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Attachment scanner failed for {ReferenceType}/{ReferenceId}; retaining quarantine record",
                    referenceType,
                    referenceId);
                scanResult = new AttachmentScanResult(
                    AttachmentScanStatus.Failed,
                    "The configured antivirus scanner could not complete the scan.");
            }

            if (!Enum.IsDefined(scanResult.Status))
            {
                scanResult = new AttachmentScanResult(
                    AttachmentScanStatus.Failed,
                    "The configured scanner returned an invalid status.");
            }

            var attachment = new Attachment(
                referenceType,
                referenceId,
                input.WarehouseId,
                input.FileName.Trim(),
                storageKey,
                prepared.Value.ContentType,
                prepared.Value.Length,
                prepared.Value.Sha256,
                actor.Value,
                input.Classification,
                scanResult.Status,
                clock.UtcNow,
                input.RetentionUntilUtc,
                input.ImmutableEvidence);

            try
            {
                context.Attachments.Add(attachment);
                await auditWriter.RecordAsync(
                    new AuditRecord(
                        WmsAuditActions.AttachmentUploaded,
                        WmsAuditEntityTypes.Attachment,
                        attachment.Id.ToString(CultureInfo.InvariantCulture),
                        attachment.WarehouseId,
                        After: new Dictionary<string, object?>
                        {
                            ["referenceType"] = attachment.ReferenceType,
                            ["referenceId"] = attachment.ReferenceId,
                            ["fileName"] = attachment.FileName,
                            ["contentType"] = attachment.ContentType,
                            ["sizeBytes"] = attachment.SizeBytes,
                            ["sha256"] = attachment.Sha256,
                            ["scanStatus"] = attachment.ScanStatus.ToString()
                        },
                        Succeeded: attachment.ScanStatus is not (AttachmentScanStatus.Suspicious or AttachmentScanStatus.Failed)),
                    cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await TryDeleteStorageAsync(storageKey, CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Attachment metadata could not be committed for {StorageKey}", storageKey);
                await TryDeleteStorageAsync(storageKey, CancellationToken.None);
                return Result.Failure<AttachmentDto>(WmsErrors.FromException(
                    exception,
                    "attachments.metadata_commit_failed",
                    "The attachment metadata could not be committed."));
            }

            var dto = Map(attachment);
            return attachment.ScanStatus is AttachmentScanStatus.Suspicious or AttachmentScanStatus.Failed
                ? Result.Failure<AttachmentDto>(WmsErrors.Dependency(
                    "attachments.quarantined",
                    "The upload was quarantined and cannot be downloaded until it is cleared.",
                    isRetryable: false))
                : Result.Success(dto);
        }
        finally
        {
            TryDelete(prepared.Value.TempPath);
        }
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(
        AttachmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!AttachmentReferenceTypes.TryNormalize(query.ReferenceType, out var referenceType))
        {
            return Result.Failure<IReadOnlyList<AttachmentDto>>(WmsErrors.Validation(
                "attachments.reference_invalid",
                "The attachment reference type is invalid."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AttachmentsRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<AttachmentDto>>();
        }

        var referenceAuthorization = await referenceAccessService.AuthorizeAsync(
            new AttachmentReferenceAccessRequest(
                referenceType,
                query.ReferenceId,
                query.WarehouseId,
                AttachmentAccessMode.Read),
            cancellationToken);
        if (referenceAuthorization.IsFailure)
        {
            return referenceAuthorization.ToFailure<IReadOnlyList<AttachmentDto>>();
        }

        var attachments = await context.Attachments
            .AsNoTracking()
            .Where(attachment => attachment.ReferenceType == referenceType &&
                                 attachment.ReferenceId == query.ReferenceId.Trim() &&
                                 attachment.WarehouseId == query.WarehouseId &&
                                 attachment.RetentionState != AttachmentRetentionState.Deleted)
            .OrderByDescending(attachment => attachment.UploadedAtUtc)
            .ThenByDescending(attachment => attachment.Id)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AttachmentDto>>(attachments.Select(Map).ToArray());
    }

    public async Task<Result<AttachmentDownload>> OpenDownloadAsync(
        int attachmentId,
        bool preview,
        CancellationToken cancellationToken = default)
    {
        var attachment = await context.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == attachmentId, cancellationToken);
        if (attachment is null || attachment.RetentionState is AttachmentRetentionState.Deleted or AttachmentRetentionState.PendingDeletion)
        {
            return Result.Failure<AttachmentDownload>(WmsErrors.NotFound(
                "attachments.not_found",
                "The attachment was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AttachmentsRead,
            attachment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AttachmentDownload>();
        }

        var referenceAuthorization = await referenceAccessService.AuthorizeAsync(
            new AttachmentReferenceAccessRequest(
                attachment.ReferenceType,
                attachment.ReferenceId,
                attachment.WarehouseId,
                AttachmentAccessMode.Read),
            cancellationToken);
        if (referenceAuthorization.IsFailure)
        {
            return referenceAuthorization.ToFailure<AttachmentDownload>();
        }

        if (!attachment.IsDownloadable ||
            (options.RequireAntivirusScan && attachment.ScanStatus != AttachmentScanStatus.Clean))
        {
            return Result.Failure<AttachmentDownload>(WmsErrors.Forbidden(
                "attachments.not_available",
                "The attachment is quarantined or has not completed security scanning."));
        }

        if (preview && !AttachmentContentValidator.IsPreviewAvailable(attachment.ContentType))
        {
            return Result.Failure<AttachmentDownload>(WmsErrors.Validation(
                "attachments.preview_unsupported",
                "A preview is not available for this attachment type."));
        }

        Stream? stream = null;
        try
        {
            stream = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.AttachmentDownloaded,
                    WmsAuditEntityTypes.Attachment,
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    attachment.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["referenceType"] = attachment.ReferenceType,
                        ["referenceId"] = attachment.ReferenceId,
                        ["preview"] = preview
                    }),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }

            logger.LogError(exception, "Attachment {AttachmentId} could not be opened", attachmentId);
            return Result.Failure<AttachmentDownload>(WmsErrors.Dependency(
                "attachments.storage_unavailable",
                "The attachment storage service could not open the file."));
        }

        return Result.Success(new AttachmentDownload(
            stream!,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            preview));
    }

    public Task<Result<AttachmentDto>> MarkEvidenceAsync(
        int attachmentId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            attachmentId,
            WmsAuditActions.AttachmentEvidenceMarked,
            (attachment, actor, now) =>
            {
                attachment.MarkImmutableEvidence(now);
                return null;
            },
            cancellationToken);

    public Task<Result<AttachmentDto>> RequestDeletionAsync(
        int attachmentId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            attachmentId,
            WmsAuditActions.AttachmentDeletionRequested,
            (attachment, actor, now) =>
            {
                attachment.RequestDeletion(actor, now);
                return null;
            },
            cancellationToken);

    private async Task<Result<AttachmentDto>> MutateAsync(
        int attachmentId,
        string auditAction,
        Func<Attachment, string, DateTimeOffset, string?> mutation,
        CancellationToken cancellationToken)
    {
        var attachment = await context.Attachments.SingleOrDefaultAsync(
            candidate => candidate.Id == attachmentId,
            cancellationToken);
        if (attachment is null)
        {
            return Result.Failure<AttachmentDto>(WmsErrors.NotFound(
                "attachments.not_found",
                "The attachment was not found."));
        }

        var actor = GetActor();
        if (actor.IsFailure)
        {
            return actor.ToFailure<AttachmentDto>();
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AttachmentsManage,
            attachment.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AttachmentDto>();
        }

        var referenceAuthorization = await referenceAccessService.AuthorizeAsync(
            new AttachmentReferenceAccessRequest(
                attachment.ReferenceType,
                attachment.ReferenceId,
                attachment.WarehouseId,
                AttachmentAccessMode.Manage),
            cancellationToken);
        if (referenceAuthorization.IsFailure)
        {
            return referenceAuthorization.ToFailure<AttachmentDto>();
        }

        try
        {
            mutation(attachment, actor.Value, clock.UtcNow);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    auditAction,
                    WmsAuditEntityTypes.Attachment,
                    attachment.Id.ToString(CultureInfo.InvariantCulture),
                    attachment.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["referenceType"] = attachment.ReferenceType,
                        ["referenceId"] = attachment.ReferenceId,
                        ["retentionState"] = attachment.RetentionState.ToString(),
                        ["immutableEvidence"] = attachment.IsImmutableEvidence
                    }),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(Map(attachment));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (exception is InvalidOperationException)
            {
                return Result.Failure<AttachmentDto>(WmsErrors.BusinessRule(
                    "attachments.mutation_not_allowed",
                    exception.Message));
            }

            return Result.Failure<AttachmentDto>(WmsErrors.FromException(
                exception,
                "attachments.mutation_failed",
                "The attachment could not be changed."));
        }
    }

    private async Task<Result<PreparedAttachment>> PrepareContentAsync(
        AttachmentUploadInput input,
        Stream content,
        CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "warecommand-attachment-" + Guid.NewGuid().ToString("N") + ".tmp");
        var signature = new byte[AttachmentContentValidator.SignatureProbeLength];
        var signatureLength = 0;
        long actualLength = 0;
        var keepStagedFile = false;

        try
        {
            await using (var temporary = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    actualLength += read;
                    if (actualLength > options.MaximumFileSizeBytes)
                    {
                        return Result.Failure<PreparedAttachment>(WmsErrors.Validation(
                            "attachments.file_size_invalid",
                            $"The file must not exceed {options.MaximumFileSizeBytes} bytes."));
                    }

                    hash.AppendData(buffer, 0, read);
                    if (signatureLength < signature.Length)
                    {
                        var take = Math.Min(read, signature.Length - signatureLength);
                        Buffer.BlockCopy(buffer, 0, signature, signatureLength, take);
                        signatureLength += take;
                    }

                    await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                var validation = AttachmentContentValidator.Validate(
                    input.FileName,
                    input.ContentType,
                    input.DeclaredLength,
                    actualLength,
                    signature.AsSpan(0, signatureLength),
                    options.MaximumFileSizeBytes);
                if (validation.IsFailure)
                {
                    return Result.Failure<PreparedAttachment>(validation.Errors);
                }

                keepStagedFile = true;
                return Result.Success(new PreparedAttachment(
                    temporaryPath,
                    actualLength,
                    AttachmentContentValidator.NormalizeContentType(input.ContentType),
                    Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception exception)
        {
            TryDelete(temporaryPath);
            logger.LogError(exception, "Attachment upload content could not be staged");
            return Result.Failure<PreparedAttachment>(WmsErrors.Dependency(
                "attachments.content_read_failed",
                "The attachment content could not be read."));
        }
        finally
        {
            // Failed validation/read paths must not leave staged content behind.
            // A successful path is deleted by UploadAsync after the adapter has
            // consumed it.
            if (!keepStagedFile)
            {
                TryDelete(temporaryPath);
            }
        }
    }

    private Result<string> GetActor() =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? Result.Success(currentUser.UserId!)
            : Result.Failure<string>(WmsErrors.Unauthorized(
                "attachments.authentication_required",
                "An authenticated user is required for attachment operations."));

    private async Task TryDeleteStorageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Attachment storage cleanup failed for {StorageKey}", storageKey);
        }
    }

    private static AttachmentDto Map(Attachment attachment) => new(
        attachment.Id,
        attachment.ReferenceType,
        attachment.ReferenceId,
        attachment.WarehouseId,
        attachment.FileName,
        attachment.ContentType,
        attachment.SizeBytes,
        attachment.Sha256,
        attachment.UploaderUserId,
        attachment.Classification,
        attachment.ScanStatus,
        attachment.RetentionState,
        attachment.UploadedAtUtc,
        attachment.RetentionUntilUtc,
        attachment.IsImmutableEvidence,
        attachment.IsDownloadable,
        AttachmentContentValidator.IsPreviewAvailable(attachment.ContentType),
        attachment.LastScanMessage,
        attachment.DeletedByUserId,
        attachment.DeletedAtUtc);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private sealed record PreparedAttachment(
        string TempPath,
        long Length,
        string ContentType,
        string Sha256);
}
