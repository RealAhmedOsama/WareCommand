using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Devices;
using Wms.Application.Labels;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Labels;

public sealed class WmsLabelTemplateService(
    WmsDbContext context,
    IClock clock,
    IAuditWriter auditWriter,
    ILogger<WmsLabelTemplateService> logger) : ILabelTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<WmsLabelTemplateDto>> SaveVersionAsync(
        WmsLabelTemplateSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = WmsLabelTemplateValidator.Validate(request.Definition);
        if (validation.Count > 0)
        {
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.Validation(
                "label.template_invalid",
                "The label template is invalid.",
                validation));
        }

        var actor = NormalizeActor(request.ActorUserId);
        if (actor is null)
        {
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.Unauthorized(
                "label.actor_required",
                "A user identity is required to save a label template."));
        }

        var definition = NormalizeDefinition(request.Definition, isActive: false);
        var duplicate = await context.LabelTemplates.AnyAsync(
            template => template.Name == definition.Name &&
                template.Version == definition.Version &&
                template.Language == definition.Language &&
                template.DocumentType == (int)definition.DocumentType &&
                template.Format == (int)definition.Format &&
                template.WarehouseId == definition.WarehouseId &&
                template.CustomerCode == definition.CustomerCode &&
                template.SupplierCode == definition.SupplierCode,
            cancellationToken);
        if (duplicate)
        {
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.Conflict(
                "label.template_version_exists",
                "That label template version already exists."));
        }

        var now = clock.UtcNow;
        var entity = ToEntity(definition, actor, now);
        context.LabelTemplates.Add(entity);
        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.LabelTemplateVersionCreated,
                WmsAuditEntityTypes.LabelTemplate,
                $"{definition.Name}:{definition.Version}",
                definition.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["name"] = definition.Name,
                    ["version"] = definition.Version,
                    ["documentType"] = definition.DocumentType.ToString(),
                    ["language"] = definition.Language,
                    ["format"] = definition.Format.ToString(),
                    ["contentHash"] = entity.ContentHash
                },
                ActorUserId: actor,
                ActorUserName: NormalizeOptional(request.ActorUserName)),
            cancellationToken);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Label template version creation conflicted for {TemplateName} v{Version}", definition.Name, definition.Version);
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.Conflict(
                "label.template_version_exists",
                "That label template version already exists."));
        }

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<WmsLabelTemplateDto>> ActivateAsync(
        string name,
        int version,
        string language,
        int? warehouseId,
        string actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default)
    {
        return await SetActivationAsync(
            name,
            version,
            language,
            warehouseId,
            actorUserId,
            actorUserName,
            WmsAuditActions.LabelTemplateActivated,
            cancellationToken);
    }

    public async Task<Result<WmsLabelTemplateDto>> RollbackAsync(
        string name,
        int version,
        string language,
        int? warehouseId,
        string actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default)
    {
        return await SetActivationAsync(
            name,
            version,
            language,
            warehouseId,
            actorUserId,
            actorUserName,
            WmsAuditActions.LabelTemplateRolledBack,
            cancellationToken);
    }

    public async Task<Result<WmsLabelTemplateDto>> ResolveActiveAsync(
        WmsLabelTemplateResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeOptional(request.Name);
        var language = NormalizeOptional(request.Language);
        if (name is null || language is null)
        {
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.Validation(
                "label.template_lookup_invalid",
                "A template name and language are required."));
        }

        var candidates = await context.LabelTemplates
            .AsNoTracking()
            .Where(template => template.IsActive &&
                template.Name == name &&
                template.DocumentType == (int)request.DocumentType &&
                template.Language == language &&
                template.Format == (int)request.Format)
            .ToListAsync(cancellationToken);

        var candidate = candidates
            .Where(template => MatchesScope(template, request))
            .OrderByDescending(template => template.WarehouseId == request.WarehouseId && request.WarehouseId.HasValue)
            .ThenByDescending(template => template.CustomerCode is not null &&
                string.Equals(template.CustomerCode, request.CustomerCode, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(template => template.SupplierCode is not null &&
                string.Equals(template.SupplierCode, request.SupplierCode, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(template => template.Version)
            .FirstOrDefault();
        return candidate is null
            ? Result.Failure<WmsLabelTemplateDto>(WmsErrors.NotFound(
                "label.template_not_found",
                "No active label template matches the requested document, language, format, and warehouse scope."))
            : Result.Success(ToDto(candidate));
    }

    public async Task<Result<IReadOnlyList<WmsLabelTemplateDto>>> ListAsync(
        WmsLabelTemplateQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var templates = context.LabelTemplates.AsNoTracking().AsQueryable();
        if (!query.IncludeInactive)
        {
            templates = templates.Where(template => template.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim();
            templates = templates.Where(template => template.Name == name);
        }

        if (query.DocumentType.HasValue)
        {
            templates = templates.Where(template => template.DocumentType == (int)query.DocumentType.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            var language = query.Language.Trim();
            templates = templates.Where(template => template.Language == language);
        }

        if (query.WarehouseId.HasValue)
        {
            templates = templates.Where(template => template.WarehouseId == query.WarehouseId || template.WarehouseId == null);
        }

        var values = await templates
            .OrderBy(template => template.Name)
            .ThenBy(template => template.Language)
            .ThenByDescending(template => template.Version)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<WmsLabelTemplateDto>>(values.Select(ToDto).ToArray());
    }

    private async Task<Result<WmsLabelTemplateDto>> SetActivationAsync(
        string name,
        int version,
        string language,
        int? warehouseId,
        string actorUserId,
        string? actorUserName,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var actor = NormalizeActor(actorUserId);
        if (actor is null)
        {
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.Unauthorized(
                "label.actor_required",
                "A user identity is required to activate a label template."));
        }

        var normalizedName = NormalizeOptional(name);
        var normalizedLanguage = NormalizeOptional(language);
        var entity = await context.LabelTemplates.SingleOrDefaultAsync(
            template => template.Name == normalizedName &&
                template.Version == version &&
                template.Language == normalizedLanguage &&
                template.WarehouseId == warehouseId,
            cancellationToken);
        if (entity is null)
        {
            return Result.Failure<WmsLabelTemplateDto>(WmsErrors.NotFound(
                "label.template_version_not_found",
                "The requested label template version was not found."));
        }

        var siblings = await context.LabelTemplates
            .Where(template => template.Name == entity.Name &&
                template.DocumentType == entity.DocumentType &&
                template.Language == entity.Language &&
                template.Format == entity.Format &&
                template.WarehouseId == entity.WarehouseId &&
                template.CustomerCode == entity.CustomerCode &&
                template.SupplierCode == entity.SupplierCode)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow;
        foreach (var sibling in siblings)
        {
            sibling.IsActive = false;
            sibling.ActivatedAtUtc = null;
            sibling.ActivatedByUserId = null;
        }

        entity.IsActive = true;
        entity.ActivatedAtUtc = now;
        entity.ActivatedByUserId = actor;
        await auditWriter.RecordAsync(
            new AuditRecord(
                auditAction,
                WmsAuditEntityTypes.LabelTemplate,
                $"{entity.Name}:{entity.Version}",
                entity.WarehouseId,
                After: new Dictionary<string, object?>
                {
                    ["name"] = entity.Name,
                    ["version"] = entity.Version,
                    ["language"] = entity.Language,
                    ["format"] = entity.Format,
                    ["contentHash"] = entity.ContentHash
                },
                ActorUserId: actor,
                ActorUserName: NormalizeOptional(actorUserName)),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    private static bool MatchesScope(
        WmsLabelTemplateEntity template,
        WmsLabelTemplateResolutionRequest request) =>
        (!template.WarehouseId.HasValue || template.WarehouseId == request.WarehouseId) &&
        (template.CustomerCode is null || string.Equals(template.CustomerCode, request.CustomerCode, StringComparison.OrdinalIgnoreCase)) &&
        (template.SupplierCode is null || string.Equals(template.SupplierCode, request.SupplierCode, StringComparison.OrdinalIgnoreCase));

    private static WmsLabelTemplateDefinition NormalizeDefinition(
        WmsLabelTemplateDefinition definition,
        bool isActive) =>
        definition with
        {
            Name = definition.Name.Trim(),
            Language = Wms.Application.Localization.WmsLocaleCatalog.Normalize(definition.Language),
            CustomerCode = NormalizeOptional(definition.CustomerCode),
            SupplierCode = NormalizeOptional(definition.SupplierCode),
            IsActive = isActive
        };

    private static WmsLabelTemplateEntity ToEntity(
        WmsLabelTemplateDefinition definition,
        string actor,
        DateTimeOffset now) =>
        new()
        {
            Name = definition.Name,
            Version = definition.Version,
            DocumentType = (int)definition.DocumentType,
            Language = definition.Language,
            WidthMillimeters = definition.WidthMillimeters,
            HeightMillimeters = definition.HeightMillimeters,
            Format = (int)definition.Format,
            Body = definition.Body,
            FieldsJson = JsonSerializer.Serialize(definition.Fields, JsonOptions),
            BarcodesJson = JsonSerializer.Serialize(definition.Barcodes, JsonOptions),
            RoutesJson = JsonSerializer.Serialize(definition.Routes, JsonOptions),
            WarehouseId = definition.WarehouseId,
            CustomerCode = definition.CustomerCode,
            SupplierCode = definition.SupplierCode,
            IsActive = false,
            ContentHash = ComputeHash(definition),
            CreatedAtUtc = now,
            CreatedByUserId = actor
        };

    private static WmsLabelTemplateDto ToDto(WmsLabelTemplateEntity entity) =>
        new(
            entity.Id,
            new WmsLabelTemplateDefinition
            {
                Name = entity.Name,
                Version = entity.Version,
                DocumentType = (WmsLabelDocumentType)entity.DocumentType,
                Language = entity.Language,
                WidthMillimeters = entity.WidthMillimeters,
                HeightMillimeters = entity.HeightMillimeters,
                Format = (WmsPrintFormat)entity.Format,
                Body = entity.Body,
                Fields = Deserialize<IReadOnlyList<WmsLabelFieldDefinition>>(entity.FieldsJson) ?? [],
                Barcodes = Deserialize<IReadOnlyList<WmsLabelBarcodeDefinition>>(entity.BarcodesJson) ?? [],
                Routes = Deserialize<IReadOnlyList<WmsPrintRoute>>(entity.RoutesJson) ?? [],
                WarehouseId = entity.WarehouseId,
                CustomerCode = entity.CustomerCode,
                SupplierCode = entity.SupplierCode,
                IsActive = entity.IsActive
            },
            entity.ContentHash,
            entity.CreatedAtUtc,
            entity.CreatedByUserId,
            entity.ActivatedAtUtc,
            entity.ActivatedByUserId);

    private static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions);

    private static string ComputeHash(WmsLabelTemplateDefinition definition)
    {
        var canonical = JsonSerializer.Serialize(
            definition with { IsActive = false },
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string? NormalizeActor(string? actor) => NormalizeOptional(actor);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class WmsLabelPrintService(
    WmsDbContext context,
    ILabelTemplateService templateService,
    IClock clock,
    IAuditWriter auditWriter,
    IEnumerable<IWmsPrintAdapter> adapters,
    ILogger<WmsLabelPrintService> logger) : ILabelPrintService
{
    public async Task<Result<WmsRenderedLabel>> PreviewAsync(
        WmsLabelPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var copies = ValidateCopies(request.Copies);
        if (copies.IsFailure)
        {
            return copies.ToFailure<WmsRenderedLabel>();
        }

        var template = await templateService.ResolveActiveAsync(request.Template, cancellationToken);
        if (template.IsFailure)
        {
            return template.ToFailure<WmsRenderedLabel>();
        }

        return WmsLabelTemplateEngine.Render(template.Value.Definition, request.Data);
    }

    public async Task<Result<WmsLabelPrintResult>> PrintAsync(
        WmsLabelPrintRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = ValidatePrintRequest(request);
        if (validation.IsFailure)
        {
            return validation.ToFailure<WmsLabelPrintResult>();
        }

        var effectiveWarehouseId = request.WarehouseId ?? request.Template.WarehouseId;
        var templateResult = await templateService.ResolveActiveAsync(
            request.Template with { WarehouseId = effectiveWarehouseId },
            cancellationToken);
        if (templateResult.IsFailure)
        {
            return templateResult.ToFailure<WmsLabelPrintResult>();
        }

        var rendered = WmsLabelTemplateEngine.Render(templateResult.Value.Definition, request.Data);
        if (rendered.IsFailure)
        {
            return rendered.ToFailure<WmsLabelPrintResult>();
        }

        var routeResult = ResolveRoute(
            templateResult.Value.Definition,
            request.Copies,
            effectiveWarehouseId,
            request.StationCode);
        if (routeResult.IsFailure)
        {
            return routeResult.ToFailure<WmsLabelPrintResult>();
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"label:{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        var existing = await context.PrintJobs.SingleOrDefaultAsync(
            job => job.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == WmsPrintJobStatus.Succeeded.ToString())
            {
                return Result.Success(ToResult(existing));
            }

            return Result.Failure<WmsLabelPrintResult>(WmsErrors.Conflict(
                "label.print_idempotency_exists",
                "A print job with this idempotency key already exists; use retry for a failed job."));
        }

        var actor = string.IsNullOrWhiteSpace(request.ActorUserId)
            ? "system"
            : request.ActorUserId.Trim();
        var now = clock.UtcNow;
        var entity = new WmsPrintJobEntity
        {
            IdempotencyKey = idempotencyKey,
            Status = WmsPrintJobStatus.Pending.ToString(),
            AttemptCount = 1,
            TemplateName = templateResult.Value.Definition.Name,
            TemplateVersion = templateResult.Value.Definition.Version,
            DocumentType = (int)templateResult.Value.Definition.DocumentType,
            Language = templateResult.Value.Definition.Language,
            Format = (int)templateResult.Value.Definition.Format,
            WarehouseId = effectiveWarehouseId,
            StationCode = NormalizeOptional(request.StationCode),
            RouteName = routeResult.Value.Name,
            Transport = (int)routeResult.Value.Transport,
            AdapterKey = NormalizeOptional(routeResult.Value.AdapterKey),
            Copies = request.Copies,
            IsReprint = request.IsReprint,
            ReprintReason = NormalizeOptional(request.ReprintReason),
            SourceReference = NormalizeOptional(request.SourceReference),
            ActorUserId = actor,
            ActorUserName = NormalizeOptional(request.ActorUserName),
            DataJson = JsonSerializer.Serialize(request.Data),
            Payload = rendered.Value.Payload,
            ContentType = rendered.Value.ContentType,
            TextPreview = rendered.Value.TextPreview,
            BrowserHtml = rendered.Value.BrowserHtml,
            PreferBrowserPdf = rendered.Value.PreferBrowserPdf,
            CreatedAtUtc = now,
            LastAttemptAtUtc = now
        };
        context.PrintJobs.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Print job idempotency conflict for {IdempotencyKey}", idempotencyKey);
            return Result.Failure<WmsLabelPrintResult>(WmsErrors.Conflict(
                "label.print_idempotency_exists",
                "A print job with this idempotency key already exists."));
        }

        return await DispatchAsync(entity, routeResult.Value, cancellationToken);
    }

    public async Task<Result<WmsLabelPrintResult>> RetryAsync(
        long jobId,
        string actorUserId,
        string? actorUserName = null,
        CancellationToken cancellationToken = default)
    {
        var actor = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        if (actor is null)
        {
            return Result.Failure<WmsLabelPrintResult>(WmsErrors.Unauthorized(
                "label.actor_required",
                "A user identity is required to retry a print job."));
        }

        var entity = await context.PrintJobs.SingleOrDefaultAsync(job => job.Id == jobId, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<WmsLabelPrintResult>(WmsErrors.NotFound(
                "label.print_job_not_found",
                "The print job was not found."));
        }

        if (entity.Status == WmsPrintJobStatus.Succeeded.ToString())
        {
            return Result.Success(ToResult(entity));
        }

        if (entity.Transport == (int)WmsPrintTransport.BrowserPdf || string.IsNullOrWhiteSpace(entity.AdapterKey))
        {
            return Result.Failure<WmsLabelPrintResult>(WmsErrors.BusinessRule(
                "label.retry_not_supported",
                "Browser-only label previews do not have a printer adapter to retry."));
        }

        var adapter = adapters.FirstOrDefault(item =>
            string.Equals(item.AdapterKey, entity.AdapterKey, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            return Result.Failure<WmsLabelPrintResult>(WmsErrors.Dependency(
                "label.adapter_unavailable",
                "The configured print adapter is not available."));
        }

        entity.Status = WmsPrintJobStatus.Pending.ToString();
        entity.AttemptCount++;
        entity.ActorUserId = actor;
        entity.ActorUserName = NormalizeOptional(actorUserName);
        entity.LastAttemptAtUtc = clock.UtcNow;
        entity.ErrorCode = null;
        entity.ErrorMessage = null;
        await context.SaveChangesAsync(cancellationToken);

        var request = new WmsPrintRequest(
            entity.TemplateName,
            (WmsPrintFormat)entity.Format,
            entity.Copies,
            entity.WarehouseId,
            entity.StationCode);
        return await DispatchAsync(entity, request, adapter, isRetry: true, cancellationToken);
    }

    private async Task<Result<WmsLabelPrintResult>> DispatchAsync(
        WmsPrintJobEntity entity,
        WmsPrintRoute route,
        CancellationToken cancellationToken)
    {
        if (route.Transport == WmsPrintTransport.BrowserPdf)
        {
            entity.Status = WmsPrintJobStatus.Succeeded.ToString();
            entity.CompletedAtUtc = clock.UtcNow;
            await auditWriter.RecordAsync(
                CreateAudit(entity, WmsAuditActions.LabelPrintSubmitted, succeeded: true),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToResult(entity));
        }

        var adapter = adapters.FirstOrDefault(item =>
            string.Equals(item.AdapterKey, route.AdapterKey, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            return await FailAsync(
                entity,
                "label.adapter_unavailable",
                "The configured print adapter is not available.",
                cancellationToken);
        }

        var request = new WmsPrintRequest(
            entity.TemplateName,
            (WmsPrintFormat)entity.Format,
            entity.Copies,
            entity.WarehouseId,
            entity.StationCode);
        return await DispatchAsync(entity, request, adapter, isRetry: false, cancellationToken);
    }

    private async Task<Result<WmsLabelPrintResult>> DispatchAsync(
        WmsPrintJobEntity entity,
        WmsPrintRequest request,
        IWmsPrintAdapter adapter,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await adapter.PrintAsync(request, entity.Payload, cancellationToken);
            if (result.IsFailure)
            {
                return await FailAsync(entity, result.ErrorCode, result.Error, cancellationToken);
            }

            entity.Status = WmsPrintJobStatus.Succeeded.ToString();
            entity.CompletedAtUtc = clock.UtcNow;
            entity.ErrorCode = null;
            entity.ErrorMessage = null;
            await auditWriter.RecordAsync(
                CreateAudit(entity, isRetry ? WmsAuditActions.LabelPrintRetried : WmsAuditActions.LabelPrintSubmitted, succeeded: true),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToResult(entity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Label print adapter failed for job {JobId}", entity.Id);
            return await FailAsync(
                entity,
                "label.print_failed",
                "The print adapter failed. The job remains available for retry.",
                cancellationToken);
        }
    }

    private async Task<Result<WmsLabelPrintResult>> FailAsync(
        WmsPrintJobEntity entity,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        entity.Status = WmsPrintJobStatus.Failed.ToString();
        entity.CompletedAtUtc = clock.UtcNow;
        entity.ErrorCode = Trim(errorCode, 150);
        entity.ErrorMessage = Trim(errorMessage, 2_000);
        await auditWriter.RecordAsync(
            CreateAudit(entity, WmsAuditActions.LabelPrintFailed, succeeded: false),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Failure<WmsLabelPrintResult>(WmsErrors.Dependency(
            entity.ErrorCode,
            entity.ErrorMessage,
            isRetryable: true));
    }

    private static Result<WmsPrintRoute> ResolveRoute(
        WmsLabelTemplateDefinition definition,
        int copies,
        int? warehouseId,
        string? stationCode)
    {
        var routes = definition.Routes.Count > 0
            ? definition.Routes
            : definition.Format == WmsPrintFormat.Pdf
                ? [new WmsPrintRoute
                {
                    Name = $"{definition.Name}-browser",
                    TemplateName = definition.Name,
                    Format = WmsPrintFormat.Pdf,
                    Transport = WmsPrintTransport.BrowserPdf,
                    WarehouseId = warehouseId,
                    StationCode = stationCode
                }]
                : [];
        return new WmsPrintRouteCatalog(routes).Resolve(
            new WmsPrintRequest(
                definition.Name,
                definition.Format,
                copies,
                warehouseId,
                stationCode));
    }

    private static Result ValidateCopies(int copies) =>
        copies is < 1 or > WmsLabelLimits.MaximumCopies
            ? Result.Failure(WmsErrors.Validation(
                "label.copies_invalid",
                "Label copies must be between 1 and 100."))
            : Result.Success();

    private static Result ValidatePrintRequest(WmsLabelPrintRequest request)
    {
        var copies = ValidateCopies(request.Copies);
        if (copies.IsFailure)
        {
            return copies;
        }

        var bounded = new (string? Value, int Maximum, string Code, string Label)[]
        {
            (request.IdempotencyKey, 250, "label.idempotency_key_invalid", "idempotency key"),
            (request.StationCode, 100, "label.station_invalid", "station code"),
            (request.SourceReference, 200, "label.source_reference_invalid", "source reference"),
            (request.ActorUserId, 450, "label.actor_invalid", "actor identity"),
            (request.ReprintReason, 500, "label.reprint_reason_invalid", "reprint reason")
        };
        foreach (var item in bounded)
        {
            if (item.Value?.Trim().Length > item.Maximum)
            {
                return Result.Failure(WmsErrors.Validation(
                    item.Code,
                    $"The {item.Label} cannot exceed {item.Maximum} characters."));
            }
        }

        if (request.IsReprint && string.IsNullOrWhiteSpace(request.ReprintReason))
        {
            return Result.Failure(WmsErrors.Validation(
                "label.reprint_reason_required",
                "A reason is required for every label reprint."));
        }

        return Result.Success();
    }

    private static AuditRecord CreateAudit(
        WmsPrintJobEntity entity,
        string action,
        bool succeeded) =>
        new(
            action,
            WmsAuditEntityTypes.PrintJob,
            entity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            entity.WarehouseId,
            After: new Dictionary<string, object?>
            {
                ["jobId"] = entity.Id,
                ["status"] = entity.Status,
                ["templateName"] = entity.TemplateName,
                ["templateVersion"] = entity.TemplateVersion,
                ["routeName"] = entity.RouteName,
                ["attemptCount"] = entity.AttemptCount,
                ["isReprint"] = entity.IsReprint,
                ["sourceReference"] = entity.SourceReference,
                ["errorCode"] = entity.ErrorCode
            },
            ActorUserId: entity.ActorUserId,
            ActorUserName: entity.ActorUserName,
            Succeeded: succeeded,
            Details: entity.ErrorMessage);

    private static WmsLabelPrintResult ToResult(WmsPrintJobEntity entity) =>
        new(
            entity.Id,
            Enum.TryParse<WmsPrintJobStatus>(entity.Status, out var status)
                ? status
                : WmsPrintJobStatus.Failed,
            entity.TemplateName,
            entity.TemplateVersion,
            entity.RouteName,
            (WmsPrintFormat)entity.Format,
            entity.Transport.HasValue ? (WmsPrintTransport)entity.Transport.Value : null,
            entity.AttemptCount,
            entity.ErrorCode,
            entity.ErrorMessage,
            new WmsRenderedLabel(
                entity.Payload,
                entity.ContentType,
                entity.TextPreview,
                entity.BrowserHtml,
                entity.PreferBrowserPdf,
                entity.TemplateName,
                entity.TemplateVersion,
                (WmsPrintFormat)entity.Format));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Trim(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? "The label print operation failed."
            : value.Trim().Length <= maximumLength
                ? value.Trim()
                : value.Trim()[..maximumLength];
}
