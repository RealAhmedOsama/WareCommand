using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.Attachments;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Jobs;
using Wms.Application.Retention;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Attachments;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Jobs;
using Wms.Infrastructure.Notifications;

namespace Wms.Infrastructure.Retention;

/// <summary>
/// Owns the retention decision boundary. It deliberately keeps immutable
/// operational history and security audit records out of every candidate set.
/// Physical attachment deletion is attempted only after an archive reference
/// has been committed; the attachment row remains as a traceable tombstone.
/// </summary>
public sealed class RetentionService : IRetentionService
{
    private const string AttachmentTargetType = "attachment";
    private const string NotificationTargetType = "notification";
    private const string IdempotencyTargetType = "idempotency";
    private const string JobExecutionTargetType = "job-execution";
    private const string JobNotificationTargetType = "job-notification";

    private readonly WmsDbContext context;
    private readonly IClock clock;
    private readonly IAttachmentStorage? attachmentStorage;
    private readonly IAuditWriter? auditWriter;
    private readonly ICurrentUser? currentUser;
    private readonly ILogger<RetentionService> logger;

    public RetentionService(
        WmsDbContext context,
        IClock clock,
        IAttachmentStorage? attachmentStorage,
        IAuditWriter? auditWriter,
        ICurrentUser? currentUser,
        ILogger<RetentionService> logger)
    {
        this.context = context;
        this.clock = clock;
        this.attachmentStorage = attachmentStorage;
        this.auditWriter = auditWriter;
        this.currentUser = currentUser;
        this.logger = logger;
    }

    public Task<Result<IReadOnlyList<RetentionClassDescriptor>>> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success<IReadOnlyList<RetentionClassDescriptor>>(RetentionCatalog.Classes));
    }

    public async Task<Result<IReadOnlyList<RetentionPolicyDto>>> ListPoliciesAsync(
        int? warehouseId = null,
        string? companyCode = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId is <= 0)
        {
            return Result.Failure<IReadOnlyList<RetentionPolicyDto>>(WmsErrors.Validation(
                "retention.warehouse_invalid",
                "The warehouse scope must be a positive identifier."));
        }

        var policies = await LoadEffectivePoliciesAsync(warehouseId, companyCode, cancellationToken);
        return Result.Success<IReadOnlyList<RetentionPolicyDto>>(
            RetentionCatalog.Classes.Select(descriptor =>
            {
                var policy = policies[descriptor.Class];
                return ToPolicyDto(policy, descriptor);
            }).ToArray());
    }

    public async Task<Result<RetentionPolicyDto>> SavePolicyAsync(
        RetentionPolicyInput input,
        CancellationToken cancellationToken = default)
    {
        RetentionClassDescriptor descriptor;
        try
        {
            descriptor = RetentionCatalog.Get(input.Class);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result.Failure<RetentionPolicyDto>(WmsErrors.Validation(
                "retention.class_invalid",
                "The retention class is invalid."));
        }

        if (input.WarehouseId is <= 0)
        {
            return Result.Failure<RetentionPolicyDto>(WmsErrors.Validation(
                "retention.warehouse_invalid",
                "The warehouse scope must be a positive identifier."));
        }

        if (input.RetentionDays < 0 || (descriptor.IsPurgeAllowed && input.RetentionDays == 0))
        {
            return Result.Failure<RetentionPolicyDto>(WmsErrors.Validation(
                "retention.days_invalid",
                "The retention period must be positive for purgeable data."));
        }

        if (descriptor.MinimumRetentionDays.HasValue &&
            input.RetentionDays < descriptor.MinimumRetentionDays.Value)
        {
            return Result.Failure<RetentionPolicyDto>(WmsErrors.BusinessRule(
                "retention.minimum_violation",
                $"The retention period for {descriptor.Key} cannot be shorter than " +
                $"{descriptor.MinimumRetentionDays.Value} days."));
        }

        if (!descriptor.IsPurgeAllowed && input.Enabled)
        {
            return Result.Failure<RetentionPolicyDto>(WmsErrors.BusinessRule(
                "retention.immutable_class",
                $"The {descriptor.Key} class is immutable and cannot be enabled for purge."));
        }

        var normalizedCompanyCode = NormalizeCompanyCode(input.CompanyCode);
        var entity = await context.RetentionPolicies.SingleOrDefaultAsync(
            policy => policy.Class == input.Class &&
                      policy.WarehouseId == input.WarehouseId &&
                      policy.CompanyCode == normalizedCompanyCode,
            cancellationToken);
        var now = clock.UtcNow;
        if (entity is null)
        {
            entity = new WmsRetentionPolicyEntity
            {
                Class = input.Class,
                PolicyKey = descriptor.Key,
                WarehouseId = input.WarehouseId,
                CompanyCode = normalizedCompanyCode,
                CreatedAtUtc = now,
                Revision = 1
            };
            context.RetentionPolicies.Add(entity);
        }
        else
        {
            entity.Revision++;
        }

        entity.RetentionDays = input.RetentionDays;
        entity.MinimumRetentionDays = descriptor.MinimumRetentionDays;
        entity.ArchiveBeforePurge = input.ArchiveBeforePurge;
        entity.ExportBeforePurge = input.ExportBeforePurge;
        entity.Enabled = input.Enabled;
        entity.UpdatedAtUtc = now;

        await RecordAuditAsync(
            WmsAuditActions.RetentionPolicyChanged,
            WmsAuditEntityTypes.RetentionPolicy,
            entity.Id == 0 ? null : entity.Id.ToString(CultureInfo.InvariantCulture),
            input.WarehouseId,
            new Dictionary<string, object?>
            {
                ["class"] = descriptor.Key,
                ["retentionDays"] = input.RetentionDays,
                ["archiveBeforePurge"] = input.ArchiveBeforePurge,
                ["exportBeforePurge"] = input.ExportBeforePurge,
                ["enabled"] = input.Enabled
            },
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ToPolicyDto(entity, descriptor));
    }

    public async Task<Result<IReadOnlyList<RetentionHoldDto>>> ListHoldsAsync(
        bool includeReleased = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.RetentionHolds.AsNoTracking().AsQueryable();
        if (!includeReleased)
        {
            query = query.Where(hold => !hold.ReleasedAtUtc.HasValue);
        }

        var holds = await query
            .OrderByDescending(hold => hold.CreatedAtUtc)
            .ThenByDescending(hold => hold.Id)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RetentionHoldDto>>(holds.Select(ToHoldDto).ToArray());
    }

    public async Task<Result<RetentionHoldDto>> CreateHoldAsync(
        RetentionHoldInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            RetentionCatalog.Get(input.Class);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result.Failure<RetentionHoldDto>(WmsErrors.Validation(
                "retention.class_invalid",
                "The retention class is invalid."));
        }

        var targetType = NormalizeRequired(input.TargetType, 100, "target type")?.ToLowerInvariant();
        var targetId = NormalizeRequired(input.TargetId, 200, "target identifier");
        var reason = NormalizeRequired(input.Reason, 2_000, "reason");
        if (targetType is null || targetId is null || reason is null)
        {
            return Result.Failure<RetentionHoldDto>(WmsErrors.Validation(
                "retention.hold_invalid",
                "A hold target type, target identifier, and reason are required."));
        }

        if (input.WarehouseId is <= 0)
        {
            return Result.Failure<RetentionHoldDto>(WmsErrors.Validation(
                "retention.warehouse_invalid",
                "The warehouse scope must be a positive identifier."));
        }

        var existing = await context.RetentionHolds.SingleOrDefaultAsync(
            hold => hold.Class == input.Class &&
                    hold.TargetType == targetType &&
                    hold.TargetId == targetId &&
                    hold.WarehouseId == input.WarehouseId &&
                    !hold.ReleasedAtUtc.HasValue,
            cancellationToken);
        if (existing is not null)
        {
            return Result.Success(ToHoldDto(existing));
        }

        var now = clock.UtcNow;
        var entity = new WmsRetentionHoldEntity
        {
            Class = input.Class,
            TargetType = targetType,
            TargetId = targetId,
            WarehouseId = input.WarehouseId,
            Reason = reason,
            CaseReference = NormalizeOptional(input.CaseReference, 200),
            CreatedByUserId = GetActor(),
            CreatedAtUtc = now
        };
        context.RetentionHolds.Add(entity);
        await RecordAuditAsync(
            WmsAuditActions.RetentionHoldCreated,
            WmsAuditEntityTypes.RetentionHold,
            null,
            input.WarehouseId,
            new Dictionary<string, object?>
            {
                ["class"] = RetentionCatalog.Get(input.Class).Key,
                ["targetType"] = targetType,
                ["targetId"] = targetId,
                ["caseReference"] = entity.CaseReference
            },
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(ToHoldDto(entity));
    }

    public async Task<Result<RetentionHoldDto>> ReleaseHoldAsync(
        long holdId,
        string? releaseReason = null,
        CancellationToken cancellationToken = default)
    {
        var hold = await context.RetentionHolds.SingleOrDefaultAsync(
            item => item.Id == holdId,
            cancellationToken);
        if (hold is null)
        {
            return Result.Failure<RetentionHoldDto>(WmsErrors.NotFound(
                "retention.hold_not_found",
                "The retention hold was not found."));
        }

        if (!hold.ReleasedAtUtc.HasValue)
        {
            hold.ReleasedAtUtc = clock.UtcNow;
            hold.ReleasedByUserId = GetActor();
            hold.ReleaseReason = NormalizeOptional(releaseReason, 2_000);
            await RecordAuditAsync(
                WmsAuditActions.RetentionHoldReleased,
                WmsAuditEntityTypes.RetentionHold,
                hold.Id.ToString(CultureInfo.InvariantCulture),
                hold.WarehouseId,
                new Dictionary<string, object?>
                {
                    ["class"] = RetentionCatalog.Get(hold.Class).Key,
                    ["targetType"] = hold.TargetType,
                    ["targetId"] = hold.TargetId,
                    ["releaseReason"] = hold.ReleaseReason
                },
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(ToHoldDto(hold));
    }

    public Task<Result<RetentionRunDto>> PreviewAsync(
        RetentionPreviewInput input,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            new RetentionRunInput(
                DryRun: true,
                AsOfUtc: input.AsOfUtc,
                WarehouseId: input.WarehouseId,
                BatchSize: input.BatchSize,
                CompanyCode: input.CompanyCode),
            cancellationToken);

    public async Task<Result<RetentionRunDto>> RunAsync(
        RetentionRunInput input,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRunInput(input);
        if (validation is not null)
        {
            return Result.Failure<RetentionRunDto>(validation);
        }

        var asOfUtc = (input.AsOfUtc ?? clock.UtcNow).ToUniversalTime();
        if (!input.DryRun)
        {
            var previewValidation = await ValidatePreviewAsync(input, asOfUtc, cancellationToken);
            if (previewValidation is not null)
            {
                return Result.Failure<RetentionRunDto>(previewValidation);
            }
        }

        var now = clock.UtcNow;
        WmsRetentionRunEntity? run = null;
        try
        {
            run = await StartRunAsync(input, asOfUtc, now, cancellationToken);
            var policies = await LoadEffectivePoliciesAsync(input.WarehouseId, input.CompanyCode, cancellationToken);
            var candidates = await BuildCandidatesAsync(input.WarehouseId, asOfUtc, policies, cancellationToken);
            var holds = await context.RetentionHolds
                .AsNoTracking()
                .Where(hold => !hold.ReleasedAtUtc.HasValue)
                .ToListAsync(cancellationToken);

            ResetRunCounts(run);
            var eligibleCandidates = new List<RetentionCandidate>();
            foreach (var candidate in candidates)
            {
                var count = run.Counts.Single(item => item.Class == candidate.Class);
                count.Examined++;
                if (IsHeld(candidate, holds))
                {
                    count.Held++;
                    continue;
                }

                if (!candidate.Policy.ArchiveBeforePurge || !candidate.Policy.ExportBeforePurge)
                {
                    count.Skipped++;
                    continue;
                }

                count.Eligible++;
                eligibleCandidates.Add(candidate);
            }

            run.ItemsExamined = run.Counts.Sum(item => item.Examined);
            run.EligibleItems = run.Counts.Sum(item => item.Eligible);
            run.HeldItems = run.Counts.Sum(item => item.Held);
            run.SkippedItems = run.Counts.Sum(item => item.Skipped);
            run.BatchCount = input.BatchSize == 0
                ? 0
                : (int)Math.Ceiling((double)eligibleCandidates.Count / input.BatchSize);
            run.CursorClass = null;
            run.CursorId = null;

            if (input.DryRun)
            {
                run.Status = RetentionRunStatus.Preview;
                run.CompletedAtUtc = clock.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
                return Result.Success(ToRunDto(run));
            }

            for (var offset = 0; offset < eligibleCandidates.Count; offset += input.BatchSize)
            {
                var batch = eligibleCandidates
                    .Skip(offset)
                    .Take(input.BatchSize)
                    .ToArray();
                foreach (var candidate in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    run.CursorClass = RetentionCatalog.Get(candidate.Class).Key;
                    run.CursorId = candidate.TargetId;
                    var outcome = await PurgeCandidateAsync(candidate, run.RunId, cancellationToken);
                    if (outcome.ArchiveCreated)
                    {
                        run.ArchivedItems++;
                    }

                    if (outcome.Purged)
                    {
                        run.PurgedItems++;
                    }
                    else if (outcome.Skipped)
                    {
                        run.SkippedItems++;
                    }
                }

                await context.SaveChangesAsync(cancellationToken);
            }

            run.Status = RetentionRunStatus.Succeeded;
            run.CompletedAtUtc = clock.UtcNow;
            run.CursorClass = null;
            run.CursorId = null;
            run.LastError = null;
            await RecordAuditAsync(
                WmsAuditActions.RetentionRunCompleted,
                WmsAuditEntityTypes.RetentionRun,
                run.RunId.ToString("N"),
                run.WarehouseId,
                new Dictionary<string, object?>
                {
                    ["dryRun"] = false,
                    ["examined"] = run.ItemsExamined,
                    ["archived"] = run.ArchivedItems,
                    ["purged"] = run.PurgedItems,
                    ["held"] = run.HeldItems,
                    ["skipped"] = run.SkippedItems
                },
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToRunDto(run));
        }
        catch (OperationCanceledException)
        {
            if (run is not null)
            {
                run.Status = RetentionRunStatus.Cancelled;
                run.CompletedAtUtc = clock.UtcNow;
                run.LastError = "Cancellation requested.";
                await context.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (run is not null)
            {
                run.Status = RetentionRunStatus.Failed;
                run.CompletedAtUtc = clock.UtcNow;
                run.LastError = Trim(exception.Message, 2_000);
                await context.SaveChangesAsync(CancellationToken.None);
            }

            logger.LogError(exception, "Retention run failed for {RunId}", input.RunId);
            return Result.Failure<RetentionRunDto>(WmsErrors.FromException(
                exception,
                "retention.run_failed",
                "The retention run could not be completed."));
        }
    }

    public async Task<Result<RetentionRunDto>> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await context.RetentionRuns
            .AsNoTracking()
            .Include(item => item.Counts)
            .SingleOrDefaultAsync(item => item.RunId == runId, cancellationToken);
        return run is null
            ? Result.Failure<RetentionRunDto>(WmsErrors.NotFound(
                "retention.run_not_found",
                "The retention run was not found."))
            : Result.Success(ToRunDto(run));
    }

    private async Task<WmsRetentionRunEntity> StartRunAsync(
        RetentionRunInput input,
        DateTimeOffset asOfUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        WmsRetentionRunEntity? run = null;
        if (input.RunId.HasValue)
        {
            run = await context.RetentionRuns
                .Include(item => item.Counts)
                .SingleOrDefaultAsync(item => item.RunId == input.RunId.Value, cancellationToken);
            if (run is not null && run.Status == RetentionRunStatus.Succeeded)
            {
                return run;
            }

            if (run is not null && run.Status == RetentionRunStatus.Running)
            {
                throw new InvalidOperationException("The retention run is already running.");
            }

            if (run is not null &&
                (run.DryRun != input.DryRun || run.AsOfUtc != asOfUtc ||
                 run.WarehouseId != input.WarehouseId ||
                 !string.Equals(run.CompanyCode, NormalizeCompanyCode(input.CompanyCode), StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "A retry must use the original retention run mode, time boundary, and warehouse scope.");
            }
        }

        run ??= new WmsRetentionRunEntity
        {
            RunId = input.RunId ?? Guid.NewGuid(),
            Status = RetentionRunStatus.Running,
            DryRun = input.DryRun,
            AllowDestructive = input.AllowDestructive,
            BackupVerified = input.BackupVerified,
            AuthorizationReference = NormalizeOptional(input.AuthorizationReference, 200),
            AsOfUtc = asOfUtc,
            WarehouseId = input.WarehouseId,
            CompanyCode = NormalizeCompanyCode(input.CompanyCode),
            BatchSize = input.BatchSize,
            RequestedByUserId = currentUser?.IsAuthenticated == true ? currentUser.UserId : "system",
            StartedAtUtc = now
        };

        if (run.Counts.Count == 0)
        {
            foreach (var descriptor in RetentionCatalog.Classes)
            {
                run.Counts.Add(new WmsRetentionRunCountEntity
                {
                    RunId = run.RunId,
                    Class = descriptor.Class,
                    IsImplemented = descriptor.IsImplemented,
                    IsPurgeAllowed = descriptor.IsPurgeAllowed
                });
            }
        }

        run.Status = RetentionRunStatus.Running;
        run.CompletedAtUtc = null;
        run.LastError = null;
        if (context.Entry(run).State == EntityState.Detached)
        {
            context.RetentionRuns.Add(run);
        }

        await RecordAuditAsync(
            WmsAuditActions.RetentionRunStarted,
            WmsAuditEntityTypes.RetentionRun,
            run.RunId.ToString("N"),
            run.WarehouseId,
            new Dictionary<string, object?>
            {
                ["dryRun"] = run.DryRun,
                ["backupVerified"] = run.BackupVerified,
                ["authorizationReference"] = run.AuthorizationReference
            },
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task<ResultError?> ValidatePreviewAsync(
        RetentionRunInput input,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (!input.AllowDestructive || !input.BackupVerified ||
            string.IsNullOrWhiteSpace(input.AuthorizationReference))
        {
            return WmsErrors.BusinessRule(
                "retention.destructive_gate",
                "Destructive retention requires explicit authorization, a verified backup, and a reference.");
        }

        if (!input.PreviewRunId.HasValue)
        {
            return WmsErrors.BusinessRule(
                "retention.preview_required",
                "Run a dry-run preview and provide its identifier before destructive execution.");
        }

        var preview = await context.RetentionRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.RunId == input.PreviewRunId.Value, cancellationToken);
        if (preview is null || preview.Status != RetentionRunStatus.Preview || !preview.DryRun)
        {
            return WmsErrors.Conflict(
                "retention.preview_invalid",
                "The supplied retention preview is missing or is not a completed dry-run.");
        }

        if (preview.AsOfUtc != asOfUtc || preview.WarehouseId != input.WarehouseId ||
            !string.Equals(preview.CompanyCode, NormalizeCompanyCode(input.CompanyCode), StringComparison.Ordinal))
        {
            return WmsErrors.Conflict(
                "retention.preview_mismatch",
                "The destructive run must use the same time boundary and warehouse scope as its preview.");
        }

        return null;
    }

    private static ResultError? ValidateRunInput(RetentionRunInput input)
    {
        if (input.WarehouseId is <= 0)
        {
            return WmsErrors.Validation(
                "retention.warehouse_invalid",
                "The warehouse scope must be a positive identifier.");
        }

        if (input.BatchSize is < 1 or > 1_000)
        {
            return WmsErrors.Validation(
                "retention.batch_invalid",
                "The retention batch size must be between 1 and 1,000.");
        }

        if (input.DryRun && (input.AllowDestructive || input.BackupVerified))
        {
            return WmsErrors.Validation(
                "retention.dry_run_flags_invalid",
                "A dry-run cannot request destructive execution or claim backup verification.");
        }

        return null;
    }

    private async Task<Dictionary<RetentionClass, EffectivePolicy>> LoadEffectivePoliciesAsync(
        int? warehouseId,
        string? companyCode,
        CancellationToken cancellationToken)
    {
        var normalizedCompanyCode = NormalizeCompanyCode(companyCode);
        var policies = await context.RetentionPolicies
            .AsNoTracking()
            .Where(policy => policy.CompanyCode == normalizedCompanyCode &&
                (warehouseId == null
                    ? policy.WarehouseId == null
                    : policy.WarehouseId == null || policy.WarehouseId == warehouseId))
            .ToListAsync(cancellationToken);
        var result = new Dictionary<RetentionClass, EffectivePolicy>();
        foreach (var descriptor in RetentionCatalog.Classes)
        {
            var configured = policies
                .Where(policy => policy.Class == descriptor.Class)
                .OrderByDescending(policy => policy.WarehouseId.HasValue && policy.WarehouseId == warehouseId)
                .ThenByDescending(policy => policy.UpdatedAtUtc)
                .FirstOrDefault();
            result[descriptor.Class] = configured is null
                ? new EffectivePolicy(
                    descriptor.Class,
                    descriptor.DefaultRetentionDays ?? 0,
                    descriptor.IsPurgeAllowed && descriptor.DefaultRetentionDays.HasValue,
                    true,
                    true,
                    false)
                : new EffectivePolicy(
                    configured.Class,
                    configured.RetentionDays,
                    configured.Enabled,
                    configured.ArchiveBeforePurge,
                    configured.ExportBeforePurge,
                    true,
                    configured.Id,
                    configured.WarehouseId,
                    string.IsNullOrWhiteSpace(configured.CompanyCode) ? null : configured.CompanyCode,
                    configured.UpdatedAtUtc);
        }

        return result;
    }

    private async Task<List<RetentionCandidate>> BuildCandidatesAsync(
        int? warehouseId,
        DateTimeOffset asOfUtc,
        IReadOnlyDictionary<RetentionClass, EffectivePolicy> policies,
        CancellationToken cancellationToken)
    {
        var candidates = new List<RetentionCandidate>();
        foreach (var descriptor in RetentionCatalog.Classes)
        {
            var policy = policies[descriptor.Class];
            if (!descriptor.IsImplemented || !descriptor.IsPurgeAllowed || !policy.Enabled || policy.RetentionDays <= 0)
            {
                continue;
            }

            var cutoff = asOfUtc.AddDays(-policy.RetentionDays);
            switch (descriptor.Class)
            {
                case RetentionClass.Documents:
                    {
                        var rows = await context.Attachments
                            .AsNoTracking()
                            .Where(attachment =>
                                (!warehouseId.HasValue || attachment.WarehouseId == warehouseId.Value) &&
                                attachment.RetentionState == AttachmentRetentionState.PendingDeletion &&
                                !attachment.IsImmutableEvidence)
                            .OrderBy(attachment => attachment.Id)
                            .ToListAsync(cancellationToken);
                        candidates.AddRange(rows
                            .Where(attachment =>
                                attachment.UploadedAtUtc <= cutoff &&
                                (!attachment.RetentionUntilUtc.HasValue || attachment.RetentionUntilUtc <= asOfUtc))
                            .Select(attachment => new RetentionCandidate(
                            descriptor.Class,
                            AttachmentTargetType,
                            attachment.Id.ToString(CultureInfo.InvariantCulture),
                            attachment.WarehouseId,
                            attachment.StorageKey,
                            attachment.Sha256,
                            JsonSerializer.Serialize(new
                            {
                                attachment.ReferenceType,
                                attachment.ReferenceId,
                                attachment.FileName,
                                attachment.ContentType,
                                attachment.SizeBytes,
                                attachment.Sha256
                            }),
                            policy)));
                        break;
                    }
                case RetentionClass.Notifications:
                    {
                        var rows = await context.Notifications
                            .Include(notification => notification.Recipients)
                            .AsNoTracking()
                            .Where(notification =>
                                !warehouseId.HasValue || notification.WarehouseId == warehouseId.Value)
                            .OrderBy(notification => notification.Id)
                            .ToListAsync(cancellationToken);
                        candidates.AddRange(rows
                            .Where(notification => notification.CreatedAtUtc <= cutoff)
                            .Where(CanPurgeNotification)
                            .Select(notification => new RetentionCandidate(
                                descriptor.Class,
                                NotificationTargetType,
                                notification.Id.ToString(CultureInfo.InvariantCulture),
                                notification.WarehouseId,
                                $"db:WmsNotifications/{notification.Id}",
                                null,
                                JsonSerializer.Serialize(new
                                {
                                    notification.Kind,
                                    notification.SourceType,
                                    notification.SourceId,
                                    notification.CreatedAtUtc
                                }),
                                policy)));
                        break;
                    }
                case RetentionClass.Idempotency:
                    {
                        var rows = await context.InventoryCommandIdempotencies
                            .AsNoTracking()
                            .Where(command =>
                                (!warehouseId.HasValue || command.WarehouseId == warehouseId.Value) &&
                                command.Status != InventoryCommandIdempotencyStatus.InProgress)
                            .OrderBy(command => command.Id)
                            .ToListAsync(cancellationToken);
                        candidates.AddRange(rows
                            .Where(command => command.CreatedAt <= cutoff.UtcDateTime && command.ExpiresAtUtc <= asOfUtc)
                            .Select(command => new RetentionCandidate(
                            descriptor.Class,
                            IdempotencyTargetType,
                            command.Id.ToString(CultureInfo.InvariantCulture),
                            command.WarehouseId,
                            $"db:InventoryCommandIdempotencies/{command.Id}",
                            command.RequestHash,
                            JsonSerializer.Serialize(new
                            {
                                command.CommandKey,
                                command.OperationType,
                                command.Status,
                                command.ExpiresAtUtc
                            }),
                            policy)));
                        break;
                    }
                case RetentionClass.Temporary:
                    {
                        var executions = await context.JobExecutions
                            .AsNoTracking()
                            .Where(execution =>
                                (!warehouseId.HasValue || execution.WarehouseId == warehouseId.Value) &&
                                execution.Status != WmsJobExecutionStatuses.Running)
                            .OrderBy(execution => execution.Id)
                            .ToListAsync(cancellationToken);
                        candidates.AddRange(executions
                            .Where(execution => execution.CreatedAtUtc <= cutoff)
                            .Select(execution => new RetentionCandidate(
                            descriptor.Class,
                            JobExecutionTargetType,
                            execution.Id.ToString(CultureInfo.InvariantCulture),
                            execution.WarehouseId,
                            $"db:WmsJobExecutions/{execution.Id}",
                            null,
                            JsonSerializer.Serialize(new
                            {
                                execution.JobName,
                                execution.Status,
                                execution.CreatedAtUtc,
                                execution.CompletedAtUtc
                            }),
                            policy)));

                        var notifications = await context.JobNotifications
                            .AsNoTracking()
                            .Where(notification =>
                                (!warehouseId.HasValue || notification.WarehouseId == warehouseId.Value) &&
                                notification.ResolvedAtUtc.HasValue)
                            .OrderBy(notification => notification.Id)
                            .ToListAsync(cancellationToken);
                        candidates.AddRange(notifications
                            .Where(notification => notification.CreatedAtUtc <= cutoff)
                            .Select(notification => new RetentionCandidate(
                            descriptor.Class,
                            JobNotificationTargetType,
                            notification.Id.ToString(CultureInfo.InvariantCulture),
                            notification.WarehouseId,
                            $"db:WmsJobNotifications/{notification.Id}",
                            null,
                            JsonSerializer.Serialize(new
                            {
                                notification.Kind,
                                notification.CreatedAtUtc,
                                notification.ResolvedAtUtc
                            }),
                            policy)));
                        break;
                    }
            }
        }

        return candidates;
    }

    private async Task<PurgeOutcome> PurgeCandidateAsync(
        RetentionCandidate candidate,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var archive = await context.RetentionArchiveReferences.SingleOrDefaultAsync(
            reference => reference.Class == candidate.Class &&
                         reference.SourceType == candidate.TargetType &&
                         reference.SourceId == candidate.TargetId,
            cancellationToken);
        var archiveCreated = false;
        if (archive is null)
        {
            archive = new WmsRetentionArchiveReferenceEntity
            {
                Class = candidate.Class,
                SourceType = candidate.TargetType,
                SourceId = candidate.TargetId,
                WarehouseId = candidate.WarehouseId,
                ArchiveLocator = Trim(candidate.ArchiveLocator, 500) ?? "unknown",
                SourceHash = Trim(candidate.SourceHash, 128),
                MetadataJson = Trim(candidate.MetadataJson, 8_000),
                RunId = runId,
                ArchivedAtUtc = clock.UtcNow
            };
            context.RetentionArchiveReferences.Add(archive);
            await context.SaveChangesAsync(cancellationToken);
            archiveCreated = true;
        }

        if (archive.PurgedAtUtc.HasValue)
        {
            return new PurgeOutcome(archiveCreated, true, false);
        }

        try
        {
            switch (candidate.TargetType)
            {
                case AttachmentTargetType:
                    {
                        if (attachmentStorage is null)
                        {
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        if (!int.TryParse(candidate.TargetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var attachmentId))
                        {
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        try
                        {
                            await attachmentStorage.DeleteAsync(candidate.ArchiveLocator, cancellationToken);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            logger.LogWarning(
                                exception,
                                "Retention could not delete attachment storage for {AttachmentId}; the archive reference remains retryable",
                                attachmentId);
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        var attachment = await context.Attachments.SingleOrDefaultAsync(
                            item => item.Id == attachmentId,
                            cancellationToken);
                        if (attachment is not null && attachment.RetentionState == AttachmentRetentionState.PendingDeletion)
                        {
                            attachment.MarkDeleted(clock.UtcNow);
                        }

                        break;
                    }
                case NotificationTargetType:
                    {
                        if (!long.TryParse(candidate.TargetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var notificationId))
                        {
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        var notification = await context.Notifications.SingleOrDefaultAsync(
                            item => item.Id == notificationId,
                            cancellationToken);
                        if (notification is not null)
                        {
                            context.Notifications.Remove(notification);
                        }

                        break;
                    }
                case IdempotencyTargetType:
                    {
                        if (!long.TryParse(candidate.TargetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var commandId))
                        {
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        var command = await context.InventoryCommandIdempotencies.SingleOrDefaultAsync(
                            item => item.Id == commandId &&
                                    item.Status != InventoryCommandIdempotencyStatus.InProgress,
                            cancellationToken);
                        if (command is not null)
                        {
                            context.InventoryCommandIdempotencies.Remove(command);
                        }

                        break;
                    }
                case JobExecutionTargetType:
                    {
                        if (!long.TryParse(candidate.TargetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var executionId))
                        {
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        var execution = await context.JobExecutions.SingleOrDefaultAsync(
                            item => item.Id == executionId &&
                                    item.Status != WmsJobExecutionStatuses.Running,
                            cancellationToken);
                        if (execution is not null)
                        {
                            context.JobExecutions.Remove(execution);
                        }

                        break;
                    }
                case JobNotificationTargetType:
                    {
                        if (!long.TryParse(candidate.TargetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jobNotificationId))
                        {
                            return new PurgeOutcome(archiveCreated, false, true);
                        }

                        var notification = await context.JobNotifications.SingleOrDefaultAsync(
                            item => item.Id == jobNotificationId &&
                                    item.ResolvedAtUtc.HasValue,
                            cancellationToken);
                        if (notification is not null)
                        {
                            context.JobNotifications.Remove(notification);
                        }

                        break;
                    }
                default:
                    return new PurgeOutcome(archiveCreated, false, true);
            }

            archive.PurgedAtUtc = clock.UtcNow;
            return new PurgeOutcome(archiveCreated, true, false);
        }
        catch (IOException exception)
        {
            logger.LogWarning(
                exception,
                "Retention could not purge {TargetType}/{TargetId}; the archive reference remains retryable",
                candidate.TargetType,
                candidate.TargetId);
            return new PurgeOutcome(archiveCreated, false, true);
        }
    }

    private async Task RecordAuditAsync(
        string action,
        string entityType,
        string? entityId,
        int? warehouseId,
        IReadOnlyDictionary<string, object?> after,
        CancellationToken cancellationToken)
    {
        if (auditWriter is null)
        {
            return;
        }

        await auditWriter.RecordAsync(
            new AuditRecord(action, entityType, entityId, warehouseId, After: after),
            cancellationToken);
    }

    private static bool CanPurgeNotification(WmsNotificationEntity notification)
    {
        if (notification.Mandatory && !notification.ResolvedAtUtc.HasValue)
        {
            return false;
        }

        return notification.Recipients.Count == 0 ||
               notification.Recipients.All(recipient =>
                   recipient.ReadAtUtc.HasValue || recipient.AcknowledgedAtUtc.HasValue);
    }

    private static bool IsHeld(
        RetentionCandidate candidate,
        IReadOnlyCollection<WmsRetentionHoldEntity> holds) =>
        holds.Any(hold =>
            hold.Class == candidate.Class &&
            (hold.WarehouseId is null || hold.WarehouseId == candidate.WarehouseId) &&
            (hold.TargetType == "*" || string.Equals(hold.TargetType, candidate.TargetType, StringComparison.OrdinalIgnoreCase)) &&
            (hold.TargetId == "*" || string.Equals(hold.TargetId, candidate.TargetId, StringComparison.OrdinalIgnoreCase)));

    private static void ResetRunCounts(WmsRetentionRunEntity run)
    {
        foreach (var count in run.Counts)
        {
            count.Examined = 0;
            count.Eligible = 0;
            count.Held = 0;
            count.Skipped = 0;
        }
    }

    private static RetentionPolicyDto ToPolicyDto(
        EffectivePolicy policy,
        RetentionClassDescriptor descriptor) =>
        new(
            policy.EntityId,
            descriptor.Class,
            descriptor.Key,
            policy.RetentionDays,
            descriptor.MinimumRetentionDays,
            policy.WarehouseId,
            string.IsNullOrWhiteSpace(policy.CompanyCode) ? null : policy.CompanyCode,
            policy.ArchiveBeforePurge,
            policy.ExportBeforePurge,
            policy.Enabled,
            policy.IsConfigured,
            descriptor.IsPurgeAllowed,
            descriptor.IsImplemented,
            policy.UpdatedAtUtc);

    private static RetentionPolicyDto ToPolicyDto(
        WmsRetentionPolicyEntity policy,
        RetentionClassDescriptor descriptor) =>
        new(
            policy.Id,
            descriptor.Class,
            descriptor.Key,
            policy.RetentionDays,
            descriptor.MinimumRetentionDays,
            policy.WarehouseId,
            string.IsNullOrWhiteSpace(policy.CompanyCode) ? null : policy.CompanyCode,
            policy.ArchiveBeforePurge,
            policy.ExportBeforePurge,
            policy.Enabled,
            true,
            descriptor.IsPurgeAllowed,
            descriptor.IsImplemented,
            policy.UpdatedAtUtc);

    private static RetentionHoldDto ToHoldDto(WmsRetentionHoldEntity hold) => new(
        hold.Id,
        hold.Class,
        hold.TargetType,
        hold.TargetId,
        hold.Reason,
        hold.WarehouseId,
        hold.CaseReference,
        hold.CreatedByUserId,
        hold.CreatedAtUtc,
        hold.ReleasedAtUtc,
        hold.ReleasedByUserId,
        !hold.ReleasedAtUtc.HasValue);

    private static RetentionRunDto ToRunDto(WmsRetentionRunEntity run) => new(
        run.RunId,
        run.Status,
        run.DryRun,
        run.AllowDestructive,
        run.BackupVerified,
        run.AuthorizationReference,
        run.AsOfUtc,
        run.WarehouseId,
        string.IsNullOrWhiteSpace(run.CompanyCode) ? null : run.CompanyCode,
        run.BatchSize,
        run.ItemsExamined,
        run.EligibleItems,
        run.HeldItems,
        run.SkippedItems,
        run.ArchivedItems,
        run.PurgedItems,
        run.BatchCount,
        run.StartedAtUtc,
        run.CompletedAtUtc,
        run.LastError,
        run.Counts
            .OrderBy(item => item.Class)
            .Select(item => new RetentionClassCountDto(
                item.Class,
                RetentionCatalog.Get(item.Class).Key,
                item.IsImplemented,
                item.IsPurgeAllowed,
                item.Examined,
                item.Eligible,
                item.Held,
                item.Skipped))
            .ToArray());

    private string GetActor() =>
        currentUser?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(currentUser.UserId)
            ? currentUser.UserId!
            : "system";

    private static string NormalizeCompanyCode(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string? NormalizeRequired(string? value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : null;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? Trim(string? value, int maximumLength) => NormalizeOptional(value, maximumLength);

    private sealed record EffectivePolicy(
        RetentionClass Class,
        int RetentionDays,
        bool Enabled,
        bool ArchiveBeforePurge,
        bool ExportBeforePurge,
        bool IsConfigured,
        long? EntityId = null,
        int? WarehouseId = null,
        string? CompanyCode = null,
        DateTimeOffset? UpdatedAtUtc = null);

    private sealed record RetentionCandidate(
        RetentionClass Class,
        string TargetType,
        string TargetId,
        int? WarehouseId,
        string ArchiveLocator,
        string? SourceHash,
        string? MetadataJson,
        EffectivePolicy Policy);

    private sealed record PurgeOutcome(bool ArchiveCreated, bool Purged, bool Skipped);
}
