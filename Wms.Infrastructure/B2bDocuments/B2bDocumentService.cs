using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.B2bDocuments;
using Wms.Application.Common;
using Wms.Application.Connectors;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.B2bDocuments;

public sealed class B2bDocumentService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IClock clock,
    IRequestContext requestContext,
    ILogger<B2bDocumentService> logger) : IB2bDocumentService
{
    private const string ManagePermission = WmsPermissions.SettingsManage;

    public async Task<Result<B2bMappingProfileDto>> SaveMappingProfileAsync(
        B2bMappingProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateMappingProfile(request);
        if (validation is not null)
        {
            return Result.Failure<B2bMappingProfileDto>(validation);
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            ManagePermission,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<B2bMappingProfileDto>();
        }

        var documentType = request.DocumentType.Trim();
        var name = request.Name.Trim();
        var existing = await context.B2bMappingProfiles.SingleOrDefaultAsync(
            profile => profile.DocumentType == documentType &&
                       profile.Name == name &&
                       profile.Version == request.Version,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<B2bMappingProfileDto>(WmsErrors.Conflict(
                "b2b.mapping_version_exists",
                "The B2B mapping version already exists and cannot be overwritten."));
        }

        var now = clock.UtcNow;
        var entity = new WmsB2bMappingProfileEntity
        {
            DocumentType = documentType,
            Name = name,
            Version = request.Version,
            Standard = request.Standard.Trim().ToLowerInvariant(),
            RulesJson = B2bCanonicalPayload.SerializeRules(request.Rules),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.B2bMappingProfiles.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(entity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save B2B mapping profile {DocumentType}/{MappingName}", documentType, name);
            return Result.Failure<B2bMappingProfileDto>(WmsErrors.FromException(
                exception,
                "b2b.mapping_save_failed",
                "The B2B mapping profile could not be saved."));
        }
    }

    public async Task<Result<TradingPartnerDto>> CreateTradingPartnerAsync(
        TradingPartnerCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidatePartner(request);
        if (validation is not null)
        {
            return Result.Failure<TradingPartnerDto>(validation);
        }

        var warehouseIds = request.AllowedWarehouseIds.Distinct().Order().ToArray();
        var authorization = await AuthorizeWarehousesAsync(warehouseIds, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<TradingPartnerDto>();
        }

        var documentTypes = request.DocumentTypes
            .Select(documentType => documentType.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var standard = request.Standard.Trim().ToLowerInvariant();
        var mapping = await context.B2bMappingProfiles.SingleOrDefaultAsync(
            profile => documentTypes.Contains(profile.DocumentType) &&
                       profile.Name == request.MappingProfileName.Trim() &&
                       profile.Version == request.MappingProfileVersion &&
                       profile.Standard == standard &&
                       profile.IsActive,
            cancellationToken);
        if (mapping is null)
        {
            return Result.Failure<TradingPartnerDto>(WmsErrors.NotFound(
                "b2b.mapping_not_found",
                "The selected B2B mapping profile is not active or does not match the partner standard."));
        }

        string credentialReference;
        try
        {
            credentialReference = ConnectorSecurityPolicy.NormalizeCredentialReference(request.CredentialReference);
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<TradingPartnerDto>(WmsErrors.Validation(
                "b2b.credential_reference_invalid",
                exception.Message));
        }

        var now = clock.UtcNow;
        var entity = new WmsTradingPartnerEntity
        {
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Standard = standard,
            CredentialReference = credentialReference,
            CredentialVersion = 1,
            AllowedWarehouseIdsJson = Serialize(warehouseIds),
            DocumentTypesJson = Serialize(documentTypes),
            MappingProfileName = mapping.Name,
            MappingProfileVersion = mapping.Version,
            RequireAcknowledgement = request.RequireAcknowledgement,
            Status = request.Activate ? "Active" : "Draft",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.TradingPartners.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(entity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create trading partner {PartnerCode}", entity.Code);
            return Result.Failure<TradingPartnerDto>(WmsErrors.FromException(
                exception,
                "b2b.partner_create_failed",
                "The trading partner could not be created."));
        }
    }

    public async Task<Result<B2bDocumentSubmitResult>> SubmitAsync(
        B2bDocumentSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        var partner = await context.TradingPartners.SingleOrDefaultAsync(
            item => item.Id == request.TradingPartnerId,
            cancellationToken);
        if (partner is null)
        {
            return Result.Failure<B2bDocumentSubmitResult>(WmsErrors.NotFound(
                "b2b.partner_not_found",
                "The trading partner was not found."));
        }

        var envelope = request.Envelope;
        var authorization = await warehouseAccessService.AuthorizeAsync(
            ManagePermission,
            envelope.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<B2bDocumentSubmitResult>();
        }

        var allowedWarehouses = DeserializeInts(partner.AllowedWarehouseIdsJson).ToHashSet();
        var payloadHash = B2bDocumentIdentity.ComputePayloadHash(envelope.Fields);
        var externalIdentityKey = B2bDocumentIdentity.BuildExternalKey(
            partner.Id,
            envelope.InterchangeControlNumber,
            envelope.DocumentControlNumber);
        var idempotencyKey = NormalizeOptional(request.IdempotencyKey);
        var existing = await context.B2bDocuments.SingleOrDefaultAsync(
            document => document.TradingPartnerId == partner.Id &&
                        (document.ExternalIdentityKey == externalIdentityKey ||
                         (idempotencyKey != null && document.IdempotencyKey == idempotencyKey)),
            cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                return Result.Failure<B2bDocumentSubmitResult>(WmsErrors.Conflict(
                    "b2b.duplicate_payload_mismatch",
                    "The interchange or idempotency key was already used with a different payload."));
            }

            return Result.Success(new B2bDocumentSubmitResult(ToDto(existing), true));
        }

        var errors = ValidateEnvelope(partner, allowedWarehouses, envelope);
        var now = clock.UtcNow;
        var entity = new WmsB2bDocumentEntity
        {
            TradingPartnerId = partner.Id,
            MessageId = envelope.MessageId,
            DocumentType = envelope.DocumentType.Trim(),
            Version = envelope.Version.Trim(),
            Direction = envelope.Direction.Trim(),
            TransportMode = envelope.TransportMode.Trim().ToLowerInvariant(),
            InterchangeControlNumber = envelope.InterchangeControlNumber.Trim(),
            GroupControlNumber = envelope.GroupControlNumber.Trim(),
            DocumentControlNumber = envelope.DocumentControlNumber.Trim(),
            ExternalIdentityKey = externalIdentityKey,
            IdempotencyKey = idempotencyKey,
            WarehouseId = envelope.WarehouseId,
            Status = errors.Count == 0 ? WmsB2bDocumentStatuses.Validated : WmsB2bDocumentStatuses.Quarantined,
            PayloadJson = B2bCanonicalPayload.Serialize(envelope.Fields),
            PayloadHash = payloadHash,
            LineCount = envelope.LineCount,
            DeclaredLineCount = envelope.DeclaredLineCount,
            ValidationErrorsJson = B2bCanonicalPayload.SerializeErrors(errors),
            AcknowledgementStatus = partner.RequireAcknowledgement ? WmsB2bAcknowledgementStatuses.Pending : "NotRequired",
            CorrelationId = WmsExecutionIdentifiers.Normalize(envelope.CorrelationId ?? requestContext.CorrelationId),
            CreatedAtUtc = now,
            ErrorCode = errors.Count == 0 ? null : "b2b.validation_failed",
            ErrorMessage = errors.Count == 0 ? null : "The B2B document was quarantined because envelope validation failed."
        };
        context.B2bDocuments.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(new B2bDocumentSubmitResult(ToDto(entity), false));
    }

    public async Task<Result<B2bAcknowledgementDto>> AcknowledgeAsync(
        B2bAcknowledgementRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await context.B2bDocuments
            .Include(item => item.TradingPartner)
            .SingleOrDefaultAsync(item => item.Id == request.DocumentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<B2bAcknowledgementDto>(WmsErrors.NotFound(
                "b2b.document_not_found",
                "The B2B document was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            ManagePermission,
            document.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<B2bAcknowledgementDto>();
        }

        var acknowledgementType = request.AcknowledgementType.Trim().ToUpperInvariant();
        if (acknowledgementType.Length is < 2 or > 50 ||
            request.Status is not (WmsB2bAcknowledgementStatuses.Generated or WmsB2bAcknowledgementStatuses.Failed))
        {
            return Result.Failure<B2bAcknowledgementDto>(WmsErrors.Validation(
                "b2b.acknowledgement_invalid",
                "Acknowledgement type and status are invalid."));
        }

        var existing = await context.B2bAcknowledgements.SingleOrDefaultAsync(
            acknowledgement => acknowledgement.DocumentId == document.Id &&
                               acknowledgement.AcknowledgementType == acknowledgementType,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Success(ToDto(existing));
        }

        var entity = new WmsB2bAcknowledgementEntity
        {
            DocumentId = document.Id,
            AcknowledgementType = acknowledgementType,
            Status = request.Status,
            ControlNumber = $"ACK-{document.Id}-{acknowledgementType}",
            ReasonCode = NormalizeOptional(request.ReasonCode, 50),
            ReasonMessage = NormalizeOptional(request.ReasonMessage, 2_000),
            CreatedAtUtc = clock.UtcNow
        };
        document.AcknowledgementStatus = request.Status == WmsB2bAcknowledgementStatuses.Generated
            ? WmsB2bAcknowledgementStatuses.Generated
            : WmsB2bAcknowledgementStatuses.Failed;
        context.B2bAcknowledgements.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    public async Task<Result<B2bDocumentDto>> ReplayAsync(
        B2bDocumentReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var document = await context.B2bDocuments
            .Include(item => item.TradingPartner)
            .SingleOrDefaultAsync(item => item.Id == request.DocumentId, cancellationToken);
        if (document is null)
        {
            return Result.Failure<B2bDocumentDto>(WmsErrors.NotFound(
                "b2b.document_not_found",
                "The B2B document was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            ManagePermission,
            document.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<B2bDocumentDto>();
        }

        if (document.Status is not (WmsB2bDocumentStatuses.Quarantined or WmsB2bDocumentStatuses.Failed))
        {
            return Result.Failure<B2bDocumentDto>(WmsErrors.Conflict(
                "b2b.replay_not_allowed",
                "Only quarantined or failed B2B documents can be replayed."));
        }

        document.Status = WmsB2bDocumentStatuses.Replayed;
        document.ReplayCount++;
        document.ErrorCode = null;
        document.ErrorMessage = null;
        document.ProcessedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(document));
    }

    private async Task<Result> AuthorizeWarehousesAsync(
        IEnumerable<int> warehouseIds,
        CancellationToken cancellationToken)
    {
        foreach (var warehouseId in warehouseIds.Distinct().Order())
        {
            var authorization = await warehouseAccessService.AuthorizeAsync(
                ManagePermission,
                warehouseId,
                cancellationToken);
            if (authorization.IsFailure)
            {
                return authorization;
            }
        }

        return Result.Success();
    }

    private static List<B2bDocumentValidationError> ValidateEnvelope(
        WmsTradingPartnerEntity partner,
        HashSet<int> allowedWarehouses,
        B2bDocumentEnvelope envelope)
    {
        var errors = new List<B2bDocumentValidationError>();
        var documentTypes = DeserializeStrings(partner.DocumentTypesJson);
        if (envelope.MessageId == Guid.Empty)
        {
            errors.Add(new("b2b.message_id_required", "The canonical message ID is required.", "messageId"));
        }

        if (!WmsB2bDocumentTypes.IsKnown(envelope.DocumentType) ||
            !documentTypes.Contains(envelope.DocumentType, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(new("b2b.document_type_invalid", "The document type is not enabled for this trading partner.", "documentType"));
        }

        if (string.IsNullOrWhiteSpace(envelope.Version) || envelope.Version.Length > 50)
        {
            errors.Add(new("b2b.version_invalid", "A bounded document version is required.", "version"));
        }

        if (envelope.Direction is not (WmsB2bDirections.Inbound or WmsB2bDirections.Outbound))
        {
            errors.Add(new("b2b.direction_invalid", "Document direction must be inbound or outbound.", "direction"));
        }

        if (!IsTransportModeKnown(envelope.TransportMode))
        {
            errors.Add(new("b2b.transport_invalid", "The document transport mode is not supported.", "transportMode"));
        }

        if (string.IsNullOrWhiteSpace(envelope.InterchangeControlNumber) ||
            string.IsNullOrWhiteSpace(envelope.GroupControlNumber) ||
            string.IsNullOrWhiteSpace(envelope.DocumentControlNumber))
        {
            errors.Add(new("b2b.control_number_required", "Interchange, group, and document control numbers are required.", "controlNumber"));
        }

        if (!allowedWarehouses.Contains(envelope.WarehouseId))
        {
            errors.Add(new("b2b.warehouse_scope_denied", "The document warehouse is not enabled for this trading partner.", "warehouseId"));
        }

        if (envelope.LineCount < 0 || envelope.DeclaredLineCount < 0 ||
            (envelope.DeclaredLineCount.HasValue && envelope.DeclaredLineCount != envelope.LineCount))
        {
            errors.Add(new("b2b.control_total_invalid", "The declared line total does not match the canonical document.", "lineCount"));
        }

        return errors;
    }

    private static ResultError? ValidateMappingProfile(B2bMappingProfileRequest request)
    {
        if (!WmsB2bDocumentTypes.IsKnown(request.DocumentType?.Trim() ?? string.Empty) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 200 ||
            request.Version < 1)
        {
            return WmsErrors.Validation("b2b.mapping_identity_invalid", "A supported document type, name, and positive version are required.");
        }

        if (!IsStandardKnown(request.Standard))
        {
            return WmsErrors.Validation("b2b.standard_invalid", "The B2B standard is canonical, X12, or EDIFACT.");
        }

        if (request.Rules is null || request.Rules.Count == 0 ||
            request.Rules.Any(rule => string.IsNullOrWhiteSpace(rule.ExternalField) ||
                                      string.IsNullOrWhiteSpace(rule.CanonicalField)))
        {
            return WmsErrors.Validation("b2b.mapping_rules_invalid", "At least one complete mapping rule is required.");
        }

        return null;
    }

    private static ResultError? ValidatePartner(TradingPartnerCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) ||
            request.Code.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length > 200)
        {
            return WmsErrors.Validation("b2b.partner_identity_invalid", "A bounded partner code and name are required.");
        }

        if (!IsStandardKnown(request.Standard))
        {
            return WmsErrors.Validation("b2b.standard_invalid", "The B2B standard is canonical, X12, or EDIFACT.");
        }

        if (request.AllowedWarehouseIds is null || request.AllowedWarehouseIds.Count == 0 ||
            request.AllowedWarehouseIds.Any(id => id <= 0))
        {
            return WmsErrors.Validation("b2b.warehouse_scope_required", "At least one allowed warehouse is required.");
        }

        if (request.DocumentTypes is null || request.DocumentTypes.Count == 0 ||
            request.DocumentTypes.Any(type => !WmsB2bDocumentTypes.IsKnown(type.Trim())))
        {
            return WmsErrors.Validation("b2b.document_types_invalid", "Every trading-partner document type must be canonical and supported.");
        }

        if (string.IsNullOrWhiteSpace(request.MappingProfileName) || request.MappingProfileVersion < 1)
        {
            return WmsErrors.Validation("b2b.mapping_reference_invalid", "An active mapping profile reference is required.");
        }

        try
        {
            _ = ConnectorSecurityPolicy.NormalizeCredentialReference(request.CredentialReference);
        }
        catch (ArgumentException exception)
        {
            return WmsErrors.Validation("b2b.credential_reference_invalid", exception.Message);
        }

        return null;
    }

    private static bool IsStandardKnown(string? value) =>
        value?.Trim().ToLowerInvariant() is WmsB2bStandards.Canonical or WmsB2bStandards.X12 or WmsB2bStandards.Edifact;

    private static bool IsTransportModeKnown(string? value) =>
        value?.Trim().ToLowerInvariant() is WmsB2bTransportModes.Sftp or
            WmsB2bTransportModes.FileDrop or WmsB2bTransportModes.Api or WmsB2bTransportModes.Webhook;

    private static B2bMappingProfileDto ToDto(WmsB2bMappingProfileEntity entity) =>
        new(
            entity.Id,
            entity.DocumentType,
            entity.Name,
            entity.Version,
            B2bCanonicalPayload.DeserializeRules(entity.RulesJson),
            entity.Standard,
            entity.IsActive,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static TradingPartnerDto ToDto(WmsTradingPartnerEntity entity) =>
        new(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Standard,
            entity.CredentialReference,
            entity.CredentialVersion,
            DeserializeInts(entity.AllowedWarehouseIdsJson),
            DeserializeStrings(entity.DocumentTypesJson),
            entity.MappingProfileName,
            entity.MappingProfileVersion,
            entity.RequireAcknowledgement,
            entity.Status,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc);

    private static B2bDocumentDto ToDto(WmsB2bDocumentEntity entity) =>
        new(
            entity.Id,
            entity.TradingPartnerId,
            entity.MessageId,
            entity.DocumentType,
            entity.Version,
            entity.Direction,
            entity.TransportMode,
            entity.InterchangeControlNumber,
            entity.GroupControlNumber,
            entity.DocumentControlNumber,
            entity.WarehouseId,
            entity.Status,
            entity.PayloadHash,
            entity.LineCount,
            entity.DeclaredLineCount,
            B2bCanonicalPayload.DeserializeErrors(entity.ValidationErrorsJson),
            entity.AcknowledgementStatus,
            entity.ReplayCount,
            entity.CorrelationId,
            entity.CreatedAtUtc,
            entity.ProcessedAtUtc,
            entity.ErrorCode,
            entity.ErrorMessage);

    private static B2bAcknowledgementDto ToDto(WmsB2bAcknowledgementEntity entity) =>
        new(
            entity.Id,
            entity.DocumentId,
            entity.AcknowledgementType,
            entity.Status,
            entity.ControlNumber,
            entity.ReasonCode,
            entity.ReasonMessage,
            entity.CreatedAtUtc,
            entity.SentAtUtc);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    private static int[] DeserializeInts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string[] DeserializeStrings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeOptional(string? value, int maximumLength = 250)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maximumLength ? trimmed[..maximumLength] : trimmed;
    }
}
