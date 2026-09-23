using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.AnomalyDetection;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Notifications;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Services;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Integrations;

namespace Wms.Infrastructure.AnomalyDetection;

public sealed class AnomalyDetectionService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    INotificationService notificationService,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<AnomalyDetectionService> logger) : IAnomalyDetectionService
{
    public const int MaximumPageSize = 100;
    private const int MaximumRowsPerSource = 1_000;
    private const int MaximumScheduledWarehouses = 250;
    private const int MaximumRelatedRecords = 1_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<IReadOnlyList<AnomalyFinding>>> DetectAsync(
        AnomalyDetectionRequest request,
        DateTimeOffset generatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AnomalyDetector.Detect(request, generatedAtUtc));
    }

    public async Task<Result<AnomalyDetectionRunDto>> RecalculateAsync(
        AnomalyRecalculationInput input,
        string? startedByUserId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.WarehouseId <= 0)
        {
            return Result.Failure<AnomalyDetectionRunDto>(WmsErrors.Validation(
                "anomaly.warehouse_invalid",
                "A positive warehouse identifier is required."));
        }

        if (startedByUserId?.Length > 450)
        {
            return Result.Failure<AnomalyDetectionRunDto>(WmsErrors.Validation(
                "anomaly.actor_invalid",
                "The run actor identifier exceeds the supported length."));
        }

        var authorization = currentUser.IsAuthenticated
            ? await AuthorizeAsync(
                WmsPermissions.AnomalyManage,
                input.WarehouseId,
                cancellationToken)
            : Result.Success();
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AnomalyDetectionRunDto>();
        }

        var toUtc = input.ToUtc?.ToUniversalTime() ?? UtcDayStart(clock.UtcNow);
        var fromUtc = input.FromUtc?.ToUniversalTime() ?? toUtc.AddDays(-7);
        if (fromUtc > toUtc || toUtc - fromUtc > TimeSpan.FromDays(AnomalyDetectionPolicy.MaximumWindowDays))
        {
            return Result.Failure<AnomalyDetectionRunDto>(WmsErrors.Validation(
                "anomaly.window_invalid",
                "The anomaly window must be ordered and no longer than thirty-one days."));
        }

        var result = await RecalculateWarehouseAsync(
            input.WarehouseId,
            fromUtc,
            toUtc,
            string.IsNullOrWhiteSpace(startedByUserId)
                ? currentUser.IsAuthenticated ? currentUser.UserId : null
                : startedByUserId.Trim(),
            cancellationToken);
        return result.IsFailure
            ? result.ToFailure<AnomalyDetectionRunDto>()
            : Result.Success(result.Value.Run);
    }

    public async Task<Result<AnomalyScheduledRunResult>> RecalculateScheduledAsync(
        CancellationToken cancellationToken = default)
    {
        var nowUtc = clock.UtcNow.ToUniversalTime();
        var toUtc = UtcDayStart(nowUtc);
        var fromUtc = toUtc.AddDays(-7);
        var warehouseIds = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.Id)
            .Select(warehouse => warehouse.Id)
            .Take(MaximumScheduledWarehouses + 1)
            .ToListAsync(cancellationToken);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        if (warehouseIds.Count > MaximumScheduledWarehouses)
        {
            warehouseIds = warehouseIds.Take(MaximumScheduledWarehouses).ToList();
            flags.Add("scheduled-warehouse-scope-capped-at-250");
        }

        var created = 0;
        var reused = 0;
        ResultError? retryableFailure = null;
        foreach (var warehouseId in warehouseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RecalculateWarehouseAsync(
                warehouseId,
                fromUtc,
                toUtc,
                startedByUserId: null,
                cancellationToken);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Anomaly detection scheduled run failed for warehouse {WarehouseId}: {ErrorCode}",
                    warehouseId,
                    result.ErrorCode);
                flags.Add($"warehouse-{warehouseId}-run-failed");
                if ((result.FirstError!.IsRetryable || result.FirstError.Type == ErrorType.Conflict) &&
                    retryableFailure is null)
                {
                    retryableFailure = result.FirstError;
                }

                continue;
            }

            if (result.Value.Run.WasReused)
            {
                reused++;
            }
            else
            {
                created++;
            }

            flags.UnionWith(result.Value.Flags);
        }

        if (await ExpireSuppressionsAsync(nowUtc, cancellationToken))
        {
            flags.Add("suppression-expiry-scan-capped-at-5000");
        }

        if (retryableFailure is not null)
        {
            return Result.Failure<AnomalyScheduledRunResult>(retryableFailure);
        }

        return Result.Success(new AnomalyScheduledRunResult(
            warehouseIds.Count,
            created,
            reused,
            flags.OrderBy(flag => flag, StringComparer.Ordinal).ToArray()));
    }

    public async Task<Result<AnomalyFindingPageDto>> SearchAsync(
        AnomalyFindingSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.WarehouseId is <= 0 || query.Page < 1 || query.PageSize is < 1 or > MaximumPageSize ||
            (query.RuleKind.HasValue && !Enum.IsDefined(query.RuleKind.Value)) ||
            (query.Severity.HasValue && !Enum.IsDefined(query.Severity.Value)) ||
            (query.Status.HasValue && !Enum.IsDefined(query.Status.Value)))
        {
            return Result.Failure<AnomalyFindingPageDto>(WmsErrors.Validation(
                "anomaly.page_invalid",
                $"Page must be positive and page size must be between 1 and {MaximumPageSize}."));
        }

        var authorization = await AuthorizeAsync(WmsPermissions.AnomalyRead, query.WarehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AnomalyFindingPageDto>();
        }

        var findings = context.AnomalyFindings.AsNoTracking().AsQueryable();
        if (query.WarehouseId.HasValue)
        {
            findings = findings.Where(finding => finding.WarehouseId == query.WarehouseId.Value);
        }
        else
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            if (!scope.HasGlobalAccess)
            {
                if (scope.WarehouseIds.Count == 0)
                {
                    return Result.Success(new AnomalyFindingPageDto(query.Page, query.PageSize, 0, []));
                }

                var ids = scope.WarehouseIds.ToArray();
                findings = findings.Where(finding => ids.Contains(finding.WarehouseId));
            }
        }

        if (query.RuleKind.HasValue)
        {
            var ruleKind = query.RuleKind.Value.ToString();
            findings = findings.Where(finding => finding.RuleKind == ruleKind);
        }

        if (query.Severity.HasValue)
        {
            var severity = query.Severity.Value.ToString();
            findings = findings.Where(finding => finding.Severity == severity);
        }

        if (query.Status.HasValue)
        {
            var status = query.Status.Value.ToString();
            findings = findings.Where(finding => finding.Status == status);
        }

        var total = await findings.CountAsync(cancellationToken);
        var itemRows = await findings
            .OrderByDescending(finding => finding.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);
        var items = new List<AnomalyFindingSearchItemDto>(itemRows.Count);
        var sourceAccess = new Dictionary<AnomalyRuleKind, bool>();
        foreach (var row in itemRows)
        {
            var kind = Parse<AnomalyRuleKind>(row.RuleKind);
            if (!sourceAccess.TryGetValue(kind, out var canRead))
            {
                canRead = await CanReadSourceAsync(kind, cancellationToken);
                sourceAccess.Add(kind, canRead);
            }

            var item = ToSearchItem(row);
            items.Add(canRead ? item : item with { SourceType = "restricted", SourceId = "restricted" });
        }
        return Result.Success(new AnomalyFindingPageDto(query.Page, query.PageSize, total, items));
    }

    public async Task<Result<AnomalyFindingDto>> GetAsync(
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 64)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Validation(
                "anomaly.fingerprint_invalid",
                "A valid anomaly fingerprint is required."));
        }

        var entity = await context.AnomalyFindings
            .AsNoTracking()
            .SingleOrDefaultAsync(finding => finding.Fingerprint == fingerprint, cancellationToken);
        if (entity is null)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.NotFound(
                "anomaly.finding_not_found",
                "The anomaly finding was not found."));
        }

        var authorization = await AuthorizeAsync(
            WmsPermissions.AnomalyRead,
            entity.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AnomalyFindingDto>();
        }

        var observationRows = await context.AnomalyFindingObservations
            .AsNoTracking()
            .Where(observation => observation.FindingId == entity.Id)
            .OrderByDescending(observation => observation.Id)
            .Take(MaximumRelatedRecords)
            .ToListAsync(cancellationToken);
        var observations = observationRows.OrderBy(observation => observation.RecordedAtUtc)
            .Select(observation => new AnomalyFindingObservationDto(
                observation.RunId,
                observation.SourceWindowFromUtc,
                observation.SourceWindowToUtc,
                observation.SourcePresent,
                observation.IsAnomalous,
                observation.ObservedValue,
                observation.ExpectedValue,
                observation.Threshold,
                observation.Explanation,
                observation.ObservedAtUtc,
                observation.RecordedAtUtc)).ToArray();
        var historyRows = await context.AnomalyFindingHistory
            .AsNoTracking()
            .Where(entry => entry.FindingId == entity.Id)
            .OrderByDescending(entry => entry.Sequence)
            .Take(MaximumRelatedRecords)
            .OrderBy(entry => entry.Sequence)
            .ToListAsync(cancellationToken);
        var history = historyRows.Select(entry => new AnomalyFindingHistoryDto(
                entry.Sequence,
                entry.Action,
                ParseNullable<AnomalyStatus>(entry.FromStatus),
                ParseNullable<AnomalyStatus>(entry.ToStatus),
                entry.AssignedToUserId,
                entry.AssignedTeamCode,
                entry.Comment,
                JsonSerializer.Deserialize<string[]>(entry.EvidenceReferencesJson, JsonOptions) ?? [],
                entry.SuppressionExpiresAtUtc,
                entry.ActorUserId,
                entry.CreatedAtUtc)).ToArray();

        var dto = ToFindingDto(entity, observations, history, clock.UtcNow);
        return await CanReadSourceAsync(dto.RuleKind, cancellationToken)
            ? Result.Success(dto)
            : Result.Success(dto with { SourceType = "restricted", SourceId = "restricted" });
    }

    public async Task<Result<AnomalyFindingDto>> TransitionAsync(
        string fingerprint,
        AnomalyDispositionRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Validation(
                "anomaly.actor_invalid",
                "An authenticated actor is required."));
        }

        var validation = AnomalyDetectionPolicy.ValidateDisposition(request, clock.UtcNow);
        if (validation.IsFailure)
        {
            return validation.ToFailure<AnomalyFindingDto>();
        }

        var finding = await GetTrackedFindingAsync(fingerprint, cancellationToken);
        if (finding.IsFailure)
        {
            return finding.ToFailure<AnomalyFindingDto>();
        }

        var authorization = await AuthorizeAsync(
            WmsPermissions.AnomalyManage,
            finding.Value.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AnomalyFindingDto>();
        }

        var current = Parse<AnomalyStatus>(finding.Value.Status);
        if (current != request.CurrentStatus)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Conflict(
                "anomaly.revision_conflict",
                "The finding changed since it was loaded. Refresh it before applying this action."));
        }

        var nowUtc = clock.UtcNow;
        if (request.NextStatus == AnomalyStatus.Suppressed)
        {
            finding.Value.SuppressedFromStatus = current.ToString();
            finding.Value.SuppressionExpiresAtUtc = request.SuppressionExpiresAtUtc;
        }
        else
        {
            finding.Value.SuppressedFromStatus = null;
            finding.Value.SuppressionExpiresAtUtc = null;
        }

        finding.Value.Status = request.NextStatus.ToString();
        finding.Value.Revision++;
        AddHistory(
            finding.Value,
            "status.changed",
            current,
            request.NextStatus,
            actorUserId,
            nowUtc,
            request.Comment,
            request.EvidenceReferences,
            request.SuppressionExpiresAtUtc);
        return await SaveInvestigationUpdateAsync(finding.Value, cancellationToken);
    }

    public async Task<Result<AnomalyFindingDto>> AssignAsync(
        string fingerprint,
        AnomalyAssignmentInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var userId = NormalizeOptional(input.AssignedToUserId, 450);
        var team = NormalizeOptional(input.AssignedTeamCode, 50)?.ToUpperInvariant();
        var comment = NormalizeOptional(input.Comment, AnomalyDetectionPolicy.MaximumCommentLength);
        if (string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450 ||
            (userId is null && team is null) || comment is null)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Validation(
                "anomaly.assignment_invalid",
                "An assignment requires a user or team, authenticated actor, and bounded comment."));
        }

        if (userId is not null && !await context.Users.AsNoTracking().AnyAsync(
                user => user.Id == userId,
                cancellationToken))
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Validation(
                "anomaly.assignee_not_found",
                "The selected assignee does not exist."));
        }

        var finding = await GetTrackedFindingAsync(fingerprint, cancellationToken);
        if (finding.IsFailure)
        {
            return finding.ToFailure<AnomalyFindingDto>();
        }

        var authorization = await AuthorizeAsync(
            WmsPermissions.AnomalyManage,
            finding.Value.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AnomalyFindingDto>();
        }

        var entity = finding.Value;
        entity.AssignedToUserId = userId;
        entity.AssignedTeamCode = team;
        entity.AssignedByUserId = actorUserId;
        entity.AssignedAtUtc = clock.UtcNow;
        entity.Revision++;
        AddHistory(
            entity,
            "assignment.changed",
            null,
            null,
            actorUserId,
            clock.UtcNow,
            comment,
            null,
            null);
        return await SaveInvestigationUpdateAsync(entity, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<AnomalyRuleSettingDto>>> ListRuleSettingsAsync(
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId is <= 0)
        {
            return Result.Failure<IReadOnlyList<AnomalyRuleSettingDto>>(WmsErrors.Validation(
                "anomaly.warehouse_invalid",
                "A positive warehouse identifier is required."));
        }

        var authorization = await AuthorizeAsync(WmsPermissions.AnomalyRead, warehouseId, cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<AnomalyRuleSettingDto>>();
        }

        var query = context.AnomalyRuleConfigurations.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
        {
            var scopeKey = WarehouseScopeKey(warehouseId.Value);
            query = query.Where(rule => rule.ScopeKey == "global" || rule.ScopeKey == scopeKey);
        }
        else
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            if (!scope.HasGlobalAccess)
            {
                var keys = scope.WarehouseIds.Select(WarehouseScopeKey).Append("global").ToArray();
                query = query.Where(rule => keys.Contains(rule.ScopeKey));
            }
        }

        var rows = await query
            .OrderBy(rule => rule.RuleKind)
            .ThenBy(rule => rule.ScopeKey)
            .ThenByDescending(rule => rule.Version)
            .ToListAsync(cancellationToken);
        var latest = rows.GroupBy(rule => rule.ScopeKey + ":" + rule.RuleKind, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(ToRuleDto)
            .ToArray();
        return Result.Success<IReadOnlyList<AnomalyRuleSettingDto>>(latest);
    }

    public async Task<Result<AnomalyRuleSettingDto>> SaveRuleSettingAsync(
        AnomalyRuleSettingInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.RuleKind) || input.Threshold < 0 || input.Threshold > 1_000_000_000_000m ||
            string.IsNullOrWhiteSpace(actorUserId) || actorUserId.Length > 450 || input.WarehouseId is <= 0)
        {
            return Result.Failure<AnomalyRuleSettingDto>(WmsErrors.Validation(
                "anomaly.rule_setting_invalid",
                "A supported rule, non-negative bounded threshold, and authenticated actor are required."));
        }

        var authorization = await AuthorizeAsync(
            WmsPermissions.AnomalyRulesManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<AnomalyRuleSettingDto>();
        }

        if (!input.WarehouseId.HasValue)
        {
            var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
            if (!scope.HasGlobalAccess)
            {
                return Result.Failure<AnomalyRuleSettingDto>(WmsErrors.Forbidden(
                    "anomaly.global_rule_scope_forbidden",
                    "Global anomaly rules require global warehouse access."));
            }
        }

        var scopeKey = input.WarehouseId.HasValue ? WarehouseScopeKey(input.WarehouseId.Value) : "global";
        var kind = input.RuleKind.ToString();
        var version = (await context.AnomalyRuleConfigurations
            .Where(rule => rule.ScopeKey == scopeKey && rule.RuleKind == kind)
            .Select(rule => (int?)rule.Version)
            .MaxAsync(cancellationToken) ?? 0) + 1;
        var entity = new AnomalyRuleConfigurationEntity
        {
            RuleKind = kind,
            WarehouseId = input.WarehouseId,
            ScopeKey = scopeKey,
            Version = version,
            Threshold = input.Threshold,
            IsEnabled = input.IsEnabled,
            UseExternalNotifications = input.UseExternalNotifications,
            CreatedByUserId = actorUserId,
            CreatedAtUtc = clock.UtcNow
        };
        context.AnomalyRuleConfigurations.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<AnomalyRuleSettingDto>(WmsErrors.Conflict(
                "anomaly.rule_version_conflict",
                "Another rule version was created concurrently. Refresh settings and retry."));
        }

        return Result.Success(ToRuleDto(entity));
    }

    private async Task<Result<(AnomalyDetectionRunDto Run, IReadOnlyList<string> Flags)>> RecalculateWarehouseAsync(
        int warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? startedByUserId,
        CancellationToken cancellationToken)
    {
        var configs = await ResolveRulesAsync(warehouseId, cancellationToken);
        var rulesByKind = configs.ToDictionary(
            rule => Enum.Parse<AnomalyRuleKind>(rule.RuleKind),
            rule => rule);
        var data = await LoadSignalsAsync(warehouseId, fromUtc, toUtc, rulesByKind, cancellationToken);
        var request = new AnomalyDetectionRequest(
            warehouseId,
            fromUtc,
            toUtc,
            data.Signals,
            new HashSet<int> { warehouseId },
            new HashSet<string> { WmsPermissions.All },
            AnomalyDetectionPolicy.MaximumFindings,
            "anomaly.adapters.v1");
        var detected = AnomalyDetector.Detect(request, clock.UtcNow);
        if (detected.IsFailure)
        {
            return detected.ToFailure<(AnomalyDetectionRunDto, IReadOnlyList<string>)>();
        }

        var signalByFingerprint = data.Signals
            .GroupBy(signal => AnomalyDetector.Fingerprint(
                signal,
                signal.RuleVersion ?? request.RuleVersion), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(signal => signal.ObservedAtUtc).First(), StringComparer.Ordinal);
        var runFlags = data.Flags.ToHashSet(StringComparer.Ordinal);
        var sourceKeys = data.Signals.Select(SourceKey).ToHashSet(StringComparer.Ordinal);
        var ruleKinds = configs.Select(rule => rule.RuleKind).ToArray();
        var priorCandidates = await context.AnomalyFindings
            .Where(finding => finding.WarehouseId == warehouseId && ruleKinds.Contains(finding.RuleKind))
            .OrderBy(finding => finding.Id)
            .Take(MaximumRelatedRecords + 1)
            .ToListAsync(cancellationToken);
        IReadOnlyList<AnomalyFindingEntity> priorFindings = [];
        if (priorCandidates.Count > MaximumRelatedRecords)
        {
            runFlags.Add("prior-finding-scan-capped-at-1000-absence-check-skipped");
        }
        else
        {
            priorFindings = priorCandidates
                .Where(finding => finding.RuleKind is nameof(AnomalyRuleKind.AgeingWork) or nameof(AnomalyRuleKind.NegativeBalance) ||
                                  (finding.ObservedAtUtc >= fromUtc && finding.ObservedAtUtc <= toUtc))
                .ToArray();
        }

        var finalFlags = runFlags.OrderBy(flag => flag, StringComparer.Ordinal).ToArray();
        var runFingerprint = ComputeRunFingerprint(warehouseId, fromUtc, toUtc, configs, data.Signals, finalFlags);
        var existingRun = await context.AnomalyDetectionRuns.AsNoTracking()
            .SingleOrDefaultAsync(run => run.WarehouseId == warehouseId &&
                                         run.InputFingerprint == runFingerprint,
                cancellationToken);
        if (existingRun is not null)
        {
            var findingIds = await context.AnomalyFindingObservations
                .AsNoTracking()
                .Where(observation => observation.RunId == existingRun.Id && observation.IsAnomalous)
                .Select(observation => observation.FindingId)
                .Distinct()
                .Take(AnomalyDetectionPolicy.MaximumFindings)
                .ToArrayAsync(cancellationToken);
            List<AnomalyFindingEntity> existingFindingsForAlerts = findingIds.Length == 0
                ? []
                : await context.AnomalyFindings.AsNoTracking()
                    .Where(finding => findingIds.Contains(finding.Id) &&
                                      finding.Status != nameof(AnomalyStatus.FalsePositive) &&
                                      finding.Status != nameof(AnomalyStatus.Resolved) &&
                                      finding.Status != nameof(AnomalyStatus.Suppressed))
                    .OrderBy(finding => finding.Id)
                    .Take(AnomalyDetectionPolicy.MaximumFindings)
                    .ToListAsync(cancellationToken);
            foreach (var finding in existingFindingsForAlerts)
            {
                var notification = await PublishFindingNotificationAsync(finding, rulesByKind, cancellationToken);
                if (notification.IsFailure)
                {
                    return notification.ToFailure<(AnomalyDetectionRunDto Run, IReadOnlyList<string> Flags)>();
                }
            }

            return Result.Success<(AnomalyDetectionRunDto Run, IReadOnlyList<string> Flags)>(
                (ToRunDto(existingRun, wasReused: true), finalFlags));
        }

        var nowUtc = clock.UtcNow;
        var run = new AnomalyDetectionRunEntity
        {
            WarehouseId = warehouseId,
            SourceWindowFromUtc = fromUtc,
            SourceWindowToUtc = toUtc,
            InputFingerprint = runFingerprint,
            RuleVersionsJson = JsonSerializer.Serialize(configs.Select(rule =>
                new { rule.RuleKind, rule.ScopeKey, rule.Version }), JsonOptions),
            DataQualityFlagsJson = JsonSerializer.Serialize(finalFlags, JsonOptions),
            SourceSignals = data.Signals.Count,
            FindingsCreated = detected.Value.Count,
            StartedByUserId = startedByUserId,
            StartedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc
        };
        var existingFingerprints = signalByFingerprint.Keys.ToArray();
        var existingFindings = existingFingerprints.Length == 0
            ? new Dictionary<string, AnomalyFindingEntity>(StringComparer.Ordinal)
            : await context.AnomalyFindings
                .Where(finding => existingFingerprints.Contains(finding.Fingerprint))
                .ToDictionaryAsync(finding => finding.Fingerprint, StringComparer.Ordinal, cancellationToken);
        var detectedByFingerprint = detected.Value.ToDictionary(finding => finding.Fingerprint, StringComparer.Ordinal);
        var anomalousSourceKeys = signalByFingerprint
            .Where(entry => detectedByFingerprint.ContainsKey(entry.Key))
            .Select(entry => SourceKey(entry.Value))
            .ToHashSet(StringComparer.Ordinal);
        var created = 0;
        var reused = 0;
        var findingsToNotify = new Dictionary<string, AnomalyFindingEntity>(StringComparer.Ordinal);
        foreach (var signalEntry in signalByFingerprint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnomalyFindingEntity? finding;
            if (existingFindings.TryGetValue(signalEntry.Key, out finding))
            {
                reused++;
            }
            else if (detectedByFingerprint.TryGetValue(signalEntry.Key, out var detectedFinding))
            {
                finding = ToEntity(detectedFinding, run);
                context.AnomalyFindings.Add(finding);
                created++;
                AddHistory(
                    finding,
                    "finding.detected",
                    null,
                    AnomalyStatus.New,
                    startedByUserId ?? "system",
                    nowUtc,
                    "Deterministic anomaly rule produced an investigation signal.",
                    null,
                    null);
            }
            else
            {
                continue;
            }

            var signal = signalEntry.Value;
            var isAnomalous = detectedByFingerprint.ContainsKey(signalEntry.Key);
            if (isAnomalous)
            {
                findingsToNotify[finding.Fingerprint] = finding;
            }

            var observation = new AnomalyFindingObservationEntity
            {
                Run = run,
                Finding = finding,
                SourceWindowFromUtc = fromUtc,
                SourceWindowToUtc = toUtc,
                SourcePresent = true,
                IsAnomalous = isAnomalous,
                ObservedValue = signal.ObservedValue,
                ExpectedValue = signal.ExpectedValue,
                Threshold = signal.Threshold,
                Explanation = signal.Explanation,
                ObservedAtUtc = signal.ObservedAtUtc,
                RecordedAtUtc = nowUtc
            };
            context.AnomalyFindingObservations.Add(observation);
        }

        foreach (var priorFinding in priorFindings)
        {
            if (anomalousSourceKeys.Contains(SourceKey(priorFinding)) &&
                !signalByFingerprint.ContainsKey(priorFinding.Fingerprint) &&
                priorFinding.Status is not (nameof(AnomalyStatus.FalsePositive) or
                    nameof(AnomalyStatus.Resolved) or nameof(AnomalyStatus.Suppressed)))
            {
                findingsToNotify[priorFinding.Fingerprint] = priorFinding;
            }
        }

        foreach (var priorFinding in priorFindings)
        {
            if (sourceKeys.Contains(SourceKey(priorFinding)) || signalByFingerprint.ContainsKey(priorFinding.Fingerprint))
            {
                continue;
            }

            context.AnomalyFindingObservations.Add(new AnomalyFindingObservationEntity
            {
                Run = run,
                Finding = priorFinding,
                SourceWindowFromUtc = fromUtc,
                SourceWindowToUtc = toUtc,
                SourcePresent = false,
                IsAnomalous = false,
                Explanation = "The source row is no longer eligible in this window; the historical finding remains unchanged.",
                ObservedAtUtc = toUtc,
                RecordedAtUtc = nowUtc
            });
        }

        run.FindingsCreated = created;
        run.FindingsReused = reused;
        context.AnomalyDetectionRuns.Add(run);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var conflictRun = await context.AnomalyDetectionRuns.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.WarehouseId == warehouseId &&
                                                   candidate.InputFingerprint == runFingerprint,
                    cancellationToken);
            if (conflictRun is not null)
            {
                return Result.Success<(AnomalyDetectionRunDto Run, IReadOnlyList<string> Flags)>(
                    (ToRunDto(conflictRun, wasReused: true), finalFlags));
            }

            throw;
        }

        foreach (var finding in findingsToNotify.Values)
        {
            var notification = await PublishFindingNotificationAsync(finding, rulesByKind, cancellationToken);
            if (notification.IsFailure)
            {
                return notification.ToFailure<(AnomalyDetectionRunDto Run, IReadOnlyList<string> Flags)>();
            }
        }

        return Result.Success<(AnomalyDetectionRunDto Run, IReadOnlyList<string> Flags)>(
            (ToRunDto(run, wasReused: false), finalFlags));
    }

    private async Task<IReadOnlyList<AnomalyRuleConfigurationEntity>> ResolveRulesAsync(
        int warehouseId,
        CancellationToken cancellationToken)
    {
        var rows = await context.AnomalyRuleConfigurations
            .AsNoTracking()
            .Where(rule => rule.ScopeKey == "global" || rule.ScopeKey == WarehouseScopeKey(warehouseId))
            .OrderByDescending(rule => rule.Version)
            .ToListAsync(cancellationToken);
        return rows.GroupBy(rule => (rule.RuleKind, rule.ScopeKey))
            .Select(group => group.First())
            .GroupBy(rule => rule.RuleKind, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(rule => rule.WarehouseId.HasValue).First())
            .Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.RuleKind, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<SignalLoadResult> LoadSignalsAsync(
        int warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Dictionary<AnomalyRuleKind, AnomalyRuleConfigurationEntity> rules,
        CancellationToken cancellationToken)
    {
        var signals = new List<AnomalySignal>();
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var nowUtc = clock.UtcNow.ToUniversalTime();

        if (TryRule(rules, AnomalyRuleKind.InventoryAdjustment, out var adjustmentRule))
        {
            var rows = await context.InventoryTransactions.AsNoTracking()
                .Where(row => row.WarehouseId == warehouseId && row.Type == InventoryTransactionType.Adjustment &&
                              row.OccurredAtUtc >= fromUtc.UtcDateTime && row.OccurredAtUtc <= toUtc.UtcDateTime)
                .OrderBy(row => row.Id).Take(MaximumRowsPerSource + 1)
                .Select(row => new { row.Id, row.QuantityDelta, row.OccurredAtUtc })
                .ToListAsync(cancellationToken);
            if (HasMore(rows.Count, AnomalyRuleKind.InventoryAdjustment, flags))
            {
                // Do not evaluate an incomplete source window.
            }
            else
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.InventoryAdjustment,
                    "inventory-transaction",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    row.QuantityDelta,
                    0m,
                    adjustmentRule,
                    AsUtc(row.OccurredAtUtc),
                    "Inventory adjustment ledger quantity delta.")));
            }
        }

        if (TryRule(rules, AnomalyRuleKind.ReversalBurst, out var reversalRule))
        {
            var rows = await context.InventoryTransactions.AsNoTracking()
                .Where(row => row.WarehouseId == warehouseId && row.Type == InventoryTransactionType.Reversal &&
                              row.OccurredAtUtc >= fromUtc.UtcDateTime && row.OccurredAtUtc <= toUtc.UtcDateTime)
                .OrderBy(row => row.Id).Take(MaximumRowsPerSource + 1)
                .Select(row => row.OccurredAtUtc).ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.ReversalBurst, flags))
            {
                foreach (var bucket in rows.GroupBy(value => new DateTime(
                             value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc)))
                {
                    var bucketEnd = new DateTimeOffset(bucket.Key.AddHours(1), TimeSpan.Zero);
                    signals.Add(Signal(
                        AnomalyRuleKind.ReversalBurst,
                        "inventory-reversal-hour",
                        bucket.Key.ToString("yyyyMMddHH", System.Globalization.CultureInfo.InvariantCulture),
                        warehouseId,
                        bucket.Count(),
                        0m,
                        reversalRule,
                        bucketEnd > toUtc ? toUtc : bucketEnd,
                        "Reversal ledger entries grouped by UTC hour."));
                }
            }
        }

        if (TryRule(rules, AnomalyRuleKind.CountVariance, out var countRule))
        {
            var rows = await context.CycleCountLines.AsNoTracking()
                .Where(line => line.WarehouseId == warehouseId && line.CountedQuantity.HasValue &&
                               (line.Task.Status == CycleCountTaskStatus.AwaitingApproval ||
                                line.Task.Status == CycleCountTaskStatus.Approved ||
                                line.Task.Status == CycleCountTaskStatus.Completed) &&
                               line.Task.SubmittedAtUtc.HasValue &&
                               line.Task.SubmittedAtUtc.Value >= fromUtc.UtcDateTime &&
                               line.Task.SubmittedAtUtc.Value <= toUtc.UtcDateTime)
                .OrderBy(line => line.Id).Take(MaximumRowsPerSource + 1)
                .Select(line => new
                {
                    line.Id,
                    line.ExpectedQuantity,
                    Counted = line.CountedQuantity!.Value,
                    line.Task.SubmittedAtUtc
                }).ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.CountVariance, flags))
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.CountVariance,
                    "cycle-count-line",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    row.Counted,
                    row.ExpectedQuantity,
                    countRule,
                    AsUtc(row.SubmittedAtUtc!.Value),
                    "Submitted cycle count line compared with the recorded expected quantity.")));
            }
        }

        if (TryRule(rules, AnomalyRuleKind.DuplicateScan, out var scanRule))
        {
            var rows = await context.ReceivingSessionScans.AsNoTracking()
                .Where(scan => scan.Session.WarehouseId == warehouseId && scan.Status == ReceivingScanStatus.Completed &&
                               scan.RequestedAtUtc >= fromUtc.UtcDateTime && scan.RequestedAtUtc <= toUtc.UtcDateTime)
                .OrderBy(scan => scan.Id).Take(MaximumRowsPerSource + 1)
                .Select(scan => new { scan.Id, scan.ReceivingSessionId, scan.RawScanValue, scan.RequestedAtUtc })
                .ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.DuplicateScan, flags))
            {
                foreach (var group in rows.GroupBy(row => (row.ReceivingSessionId, row.RawScanValue),
                             new SessionScanComparer()))
                {
                    if (group.Count() < 2)
                    {
                        continue;
                    }

                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key.RawScanValue)))
                        .ToLowerInvariant();
                    signals.Add(Signal(
                        AnomalyRuleKind.DuplicateScan,
                        "receiving-scan-group",
                        $"{group.Key.ReceivingSessionId}:{hash[..24]}",
                        warehouseId,
                        group.Count(),
                        1m,
                        scanRule,
                        AsUtc(group.Max(row => row.RequestedAtUtc)),
                        "Repeated completed scans in one receiving session; scan content is represented by a one-way digest."));
                }
            }
        }

        if (TryRule(rules, AnomalyRuleKind.ReceivingDiscrepancy, out var receivingRule))
        {
            var rows = await context.InboundExceptions.AsNoTracking()
                .Where(row => row.WarehouseId == warehouseId && row.ExpectedBaseQuantity.HasValue &&
                              row.ActualBaseQuantity.HasValue && row.Status != InboundExceptionStatus.Cancelled &&
                              row.Status != InboundExceptionStatus.Resolved &&
                              row.CreatedAt >= fromUtc.UtcDateTime && row.CreatedAt <= toUtc.UtcDateTime)
                .OrderBy(row => row.Id).Take(MaximumRowsPerSource + 1)
                .Select(row => new
                {
                    row.Id,
                    Expected = row.ExpectedBaseQuantity!.Value,
                    Actual = row.ActualBaseQuantity!.Value,
                    row.CreatedAt
                })
                .ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.ReceivingDiscrepancy, flags))
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.ReceivingDiscrepancy,
                    "inbound-exception",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    row.Actual,
                    row.Expected,
                    receivingRule,
                    AsUtc(row.CreatedAt),
                    "Open inbound exception expected and actual base quantities.")));
            }
        }

        if (TryRule(rules, AnomalyRuleKind.ShippingDiscrepancy, out var shippingRule))
        {
            var rows = await context.OutboundExceptions.AsNoTracking()
                .Where(row => row.WarehouseId == warehouseId && row.ExpectedBaseQuantity.HasValue &&
                              row.ActualBaseQuantity.HasValue && row.Status != OutboundExceptionStatus.Cancelled &&
                              row.Status != OutboundExceptionStatus.Resolved &&
                              row.CreatedAt >= fromUtc.UtcDateTime && row.CreatedAt <= toUtc.UtcDateTime)
                .OrderBy(row => row.Id).Take(MaximumRowsPerSource + 1)
                .Select(row => new
                {
                    row.Id,
                    Expected = row.ExpectedBaseQuantity!.Value,
                    Actual = row.ActualBaseQuantity!.Value,
                    row.CreatedAt
                })
                .ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.ShippingDiscrepancy, flags))
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.ShippingDiscrepancy,
                    "outbound-exception",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    row.Actual,
                    row.Expected,
                    shippingRule,
                    AsUtc(row.CreatedAt),
                    "Open outbound exception expected and actual base quantities.")));
            }
        }

        if (TryRule(rules, AnomalyRuleKind.AgeingWork, out var ageingRule))
        {
            var rows = await context.WarehouseWorks.AsNoTracking()
                .Where(work => work.WarehouseId == warehouseId &&
                               work.Status != WarehouseWorkStatus.Completed &&
                               work.Status != WarehouseWorkStatus.Cancelled &&
                               (work.DueAtUtc ?? work.CreatedAt) < nowUtc.UtcDateTime)
                .OrderBy(work => work.DueAtUtc ?? work.CreatedAt).ThenBy(work => work.Id)
                .Take(MaximumRowsPerSource + 1)
                .Select(work => new { work.Id, Started = work.DueAtUtc ?? work.CreatedAt })
                .ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.AgeingWork, flags))
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.AgeingWork,
                    "warehouse-work",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    (decimal)Math.Floor((nowUtc.UtcDateTime - row.Started).TotalHours),
                    0m,
                    ageingRule,
                    nowUtc,
                    "Nonterminal warehouse work age measured from due time or creation time.")));
            }
        }

        if (TryRule(rules, AnomalyRuleKind.IntegrationFailure, out var integrationRule))
        {
            var candidates = await context.IntegrationOutbox.AsNoTracking()
                .Where(row => row.WarehouseId == warehouseId && row.Status == "DeadLettered" &&
                              row.DeadLetteredAtUtc.HasValue)
                .OrderBy(row => row.Id).Take(MaximumRowsPerSource + 1)
                .Select(row => new { row.Id, row.AttemptCount, At = row.DeadLetteredAtUtc!.Value })
                .ToListAsync(cancellationToken);
            var rows = candidates.Where(row => row.At >= fromUtc && row.At <= toUtc).ToArray();
            if (!HasMore(candidates.Count, AnomalyRuleKind.IntegrationFailure, flags))
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.IntegrationFailure,
                    "integration-outbox-dead-letter",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    1m,
                    0m,
                    integrationRule,
                    row.At,
                    $"A warehouse-scoped outbox message reached terminal dead-letter after {row.AttemptCount} attempts.")));
            }

            flags.Add("integration-inbox-dead-letters-excluded-no-warehouse-key");
        }

        if (TryRule(rules, AnomalyRuleKind.NegativeBalance, out var balanceRule))
        {
            var rows = await context.InventoryBalances.AsNoTracking()
                .Where(balance => balance.WarehouseId == warehouseId && balance.OnHandQuantity < 0)
                .OrderBy(balance => balance.Id).Take(MaximumRowsPerSource + 1)
                .Select(balance => new { balance.Id, balance.OnHandQuantity })
                .ToListAsync(cancellationToken);
            if (!HasMore(rows.Count, AnomalyRuleKind.NegativeBalance, flags))
            {
                signals.AddRange(rows.Select(row => Signal(
                    AnomalyRuleKind.NegativeBalance,
                    "inventory-balance-snapshot",
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    warehouseId,
                    row.OnHandQuantity,
                    0m,
                    balanceRule,
                    nowUtc,
                    "Current inventory balance snapshot has negative on-hand quantity.")));
            }
        }

        if (signals.Count > AnomalyDetectionPolicy.MaximumSignals)
        {
            flags.Add("run-signal-cap-exceeded-all-rules-skipped");
            signals.Clear();
        }

        return new SignalLoadResult(
            signals.OrderBy(signal => signal.RuleKind).ThenBy(signal => signal.ReferenceId, StringComparer.Ordinal).ToArray(),
            flags.OrderBy(flag => flag, StringComparer.Ordinal).ToArray());
    }

    private async Task<Result> PublishFindingNotificationAsync(
        AnomalyFindingEntity finding,
        Dictionary<AnomalyRuleKind, AnomalyRuleConfigurationEntity> rules,
        CancellationToken cancellationToken)
    {
        AnomalyRuleConfigurationEntity? matchingRule = null;
        if (Enum.TryParse<AnomalyRuleKind>(finding.RuleKind, out var kind))
        {
            if (rules.TryGetValue(kind, out var currentRule) &&
                BuildRuleVersion(currentRule) == finding.RuleVersion)
            {
                matchingRule = currentRule;
            }
            else
            {
                var candidates = await context.AnomalyRuleConfigurations.AsNoTracking()
                    .Where(rule => rule.RuleKind == finding.RuleKind &&
                                   (rule.ScopeKey == "global" ||
                                    rule.ScopeKey == WarehouseScopeKey(finding.WarehouseId)))
                    .ToListAsync(cancellationToken);
                matchingRule = candidates.FirstOrDefault(rule => BuildRuleVersion(rule) == finding.RuleVersion);
            }
        }

        var external = matchingRule?.UseExternalNotifications == true;
        var channels = external
            ? new[] { NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.Webhook }
            : [NotificationChannel.InApp];
        var status = await notificationService.PublishAsync(new NotificationPublishInput(
            "anomaly.finding",
            finding.Severity == nameof(AnomalySeverity.High) ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            "Operational anomaly detected",
            "تم اكتشاف إشارة تشغيلية غير معتادة",
            $"{finding.RuleKind}: {finding.Explanation}",
            $"{finding.RuleKind}: {finding.Explanation}",
            new NotificationAudience(
                Roles: [WmsRoleNames.Administrator, WmsRoleNames.WarehouseManager, WmsRoleNames.InventoryController],
                WarehouseId: finding.WarehouseId),
            channels,
            RequiredPermission: WmsPermissions.AnomalyRead,
            SourceType: "anomaly-finding",
            SourceId: finding.Fingerprint,
            DeepLink: $"/api/anomalies/{finding.Fingerprint}",
            DeduplicationKey: $"anomaly:{finding.Fingerprint}"), cancellationToken);
        if (status.IsFailure)
        {
            logger.LogWarning(
                "Anomaly finding notification could not be published for fingerprint {Fingerprint}: {ErrorCode}",
                finding.Fingerprint,
                status.ErrorCode);
            return Result.Failure(status.Errors);
        }

        return Result.Success();
    }

    private async Task<bool> ExpireSuppressionsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var candidates = await context.AnomalyFindings
            .Where(finding => finding.Status == nameof(AnomalyStatus.Suppressed))
            .OrderBy(finding => finding.Id)
            .Take(5_000)
            .ToListAsync(cancellationToken);
        var lastCandidateId = candidates.Count == 0 ? 0 : candidates[^1].Id;
        if (candidates.Count == 5_000 &&
            await context.AnomalyFindings.AsNoTracking().AnyAsync(
                finding => finding.Status == nameof(AnomalyStatus.Suppressed) &&
                           finding.Id > lastCandidateId,
                cancellationToken))
        {
            logger.LogWarning("Anomaly suppression expiry exceeded its 5,000-finding batch cap.");
            return true;
        }

        var expired = candidates.Where(finding => finding.SuppressionExpiresAtUtc <= nowUtc).ToArray();
        foreach (var finding in expired)
        {
            var next = ParseNullable<AnomalyStatus>(finding.SuppressedFromStatus) ?? AnomalyStatus.Investigating;
            finding.Status = next.ToString();
            finding.SuppressedFromStatus = null;
            finding.SuppressionExpiresAtUtc = null;
            finding.Revision++;
            AddHistory(
                finding,
                "suppression.expired",
                AnomalyStatus.Suppressed,
                next,
                "system",
                nowUtc,
                "Time-bounded suppression expired.",
                null,
                null);
        }

        if (expired.Length > 0)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (ConcurrencyConflictException exception)
            {
                logger.LogInformation(exception, "Concurrent anomaly suppression expiry was detected.");
                context.ChangeTracker.Clear();
            }
        }

        return false;
    }

    private async Task<Result<AnomalyFindingEntity>> GetTrackedFindingAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length != 64)
        {
            return Result.Failure<AnomalyFindingEntity>(WmsErrors.Validation(
                "anomaly.fingerprint_invalid",
                "A valid anomaly fingerprint is required."));
        }

        var finding = await context.AnomalyFindings.SingleOrDefaultAsync(
            entity => entity.Fingerprint == fingerprint,
            cancellationToken);
        return finding is null
            ? Result.Failure<AnomalyFindingEntity>(WmsErrors.NotFound(
                "anomaly.finding_not_found",
                "The anomaly finding was not found."))
            : Result.Success(finding);
    }

    private async Task<Result<AnomalyFindingDto>> SaveInvestigationUpdateAsync(
        AnomalyFindingEntity entity,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Conflict(
                "anomaly.revision_conflict",
                "The finding changed while this action was being saved. Refresh it and retry."));
        }
        catch (DbUpdateException)
        {
            return Result.Failure<AnomalyFindingDto>(WmsErrors.Conflict(
                "anomaly.history_conflict",
                "The investigation history changed concurrently. Refresh the finding and retry."));
        }

        context.Entry(entity).State = EntityState.Detached;
        return await GetAsync(entity.Fingerprint, cancellationToken);
    }

    private async Task<Result> AuthorizeAsync(
        string permission,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        return await warehouseAccessService.AuthorizeAsync(permission, warehouseId, cancellationToken);
    }

    private static bool TryRule(
        Dictionary<AnomalyRuleKind, AnomalyRuleConfigurationEntity> rules,
        AnomalyRuleKind kind,
        out AnomalyRuleConfigurationEntity rule) => rules.TryGetValue(kind, out rule!);

    private static bool HasMore(int count, AnomalyRuleKind kind, HashSet<string> flags)
    {
        if (count <= MaximumRowsPerSource)
        {
            return false;
        }

        flags.Add($"{kind}-source-capped-at-{MaximumRowsPerSource}-rule-skipped");
        return true;
    }

    private static AnomalySignal Signal(
        AnomalyRuleKind kind,
        string sourceType,
        string sourceId,
        int warehouseId,
        decimal observed,
        decimal expected,
        AnomalyRuleConfigurationEntity rule,
        DateTimeOffset observedAtUtc,
        string explanation) =>
        new(
            kind,
            sourceType,
            sourceId,
            warehouseId,
            observed,
            expected,
            rule.Threshold,
            observedAtUtc,
            RuleVersion: BuildRuleVersion(rule),
            Explanation: explanation);

    private static string BuildRuleVersion(AnomalyRuleConfigurationEntity rule)
    {
        var scopeToken = Convert.ToBase64String(Encoding.UTF8.GetBytes(rule.ScopeKey))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{rule.RuleKind.ToLowerInvariant()}.{scopeToken}.v{rule.Version}";
    }

    private static string ComputeRunFingerprint(
        int warehouseId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        IReadOnlyList<AnomalyRuleConfigurationEntity> configs,
        IReadOnlyList<AnomalySignal> signals,
        IReadOnlyList<string> flags)
    {
        var input = new StringBuilder()
            .Append(warehouseId).Append('|')
            .Append(fromUtc.ToUniversalTime().ToString("O")).Append('|')
            .Append(toUtc.ToUniversalTime().ToString("O"));
        foreach (var rule in configs.OrderBy(value => value.RuleKind, StringComparer.Ordinal))
        {
            input.Append('|').Append(rule.RuleKind).Append(':').Append(rule.ScopeKey)
                .Append(':').Append(rule.Version).Append(':')
                .Append(rule.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append(rule.IsEnabled).Append(':').Append(rule.UseExternalNotifications);
        }

        foreach (var signal in signals.OrderBy(value => value.RuleKind)
                     .ThenBy(value => value.ReferenceType, StringComparer.Ordinal)
                     .ThenBy(value => value.ReferenceId, StringComparer.Ordinal))
        {
            input.Append('|').Append(AnomalyDetector.Fingerprint(signal, signal.RuleVersion ?? "rules.v1"))
                .Append(':').Append(signal.ObservedValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append(signal.ExpectedValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append(signal.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (signal.RuleKind is not (AnomalyRuleKind.AgeingWork or AnomalyRuleKind.NegativeBalance))
            {
                input.Append(':').Append(signal.ObservedAtUtc.ToUniversalTime().ToString("O"));
            }
        }

        foreach (var flag in flags.OrderBy(value => value, StringComparer.Ordinal))
        {
            input.Append("|flag:").Append(flag);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToString())))
            .ToLowerInvariant();
    }

    private static AnomalyFindingEntity ToEntity(AnomalyFinding finding, AnomalyDetectionRunEntity run) => new()
    {
        Fingerprint = finding.Fingerprint,
        WarehouseId = finding.WarehouseId,
        RuleKind = finding.RuleKind.ToString(),
        Severity = finding.Severity.ToString(),
        Status = AnomalyStatus.New.ToString(),
        RuleVersion = finding.RuleVersion,
        SourceType = finding.ReferenceType,
        SourceId = finding.ReferenceId,
        FirstDetectionRun = run,
        SourceWindowFromUtc = finding.SourceWindowFromUtc ?? run.SourceWindowFromUtc,
        SourceWindowToUtc = finding.SourceWindowToUtc ?? run.SourceWindowToUtc,
        ObservedValue = finding.ObservedValue,
        ExpectedValue = finding.ExpectedValue,
        Threshold = finding.Threshold,
        Explanation = finding.Explanation,
        ObservedAtUtc = finding.ObservedAtUtc,
        FirstDetectedAtUtc = finding.GeneratedAtUtc,
        Revision = 1
    };

    private void AddHistory(
        AnomalyFindingEntity finding,
        string action,
        AnomalyStatus? fromStatus,
        AnomalyStatus? toStatus,
        string actorUserId,
        DateTimeOffset createdAtUtc,
        string? comment,
        IReadOnlyList<string>? evidenceReferences,
        DateTimeOffset? suppressionExpiresAtUtc,
        string? assignedToUserId = null,
        string? assignedTeamCode = null)
    {
        var nextSequence = finding.History.Count == 0
            ? context.AnomalyFindingHistory.Where(entry => entry.FindingId == finding.Id)
                .Select(entry => (int?)entry.Sequence).Max() ?? 0
            : finding.History.Max(entry => entry.Sequence);
        finding.History.Add(new AnomalyFindingHistoryEntity
        {
            Sequence = nextSequence + 1,
            Action = action,
            FromStatus = fromStatus?.ToString(),
            ToStatus = toStatus?.ToString(),
            AssignedToUserId = assignedToUserId ?? finding.AssignedToUserId,
            AssignedTeamCode = assignedTeamCode ?? finding.AssignedTeamCode,
            Comment = NormalizeOptional(comment, AnomalyDetectionPolicy.MaximumCommentLength),
            EvidenceReferencesJson = JsonSerializer.Serialize(evidenceReferences ?? [], JsonOptions),
            SuppressionExpiresAtUtc = suppressionExpiresAtUtc,
            ActorUserId = actorUserId,
            CreatedAtUtc = createdAtUtc
        });
    }

    private static AnomalyFindingSearchItemDto ToSearchItem(AnomalyFindingEntity finding) => new(
        finding.Fingerprint,
        finding.WarehouseId,
        Parse<AnomalyRuleKind>(finding.RuleKind),
        Parse<AnomalySeverity>(finding.Severity),
        Parse<AnomalyStatus>(finding.Status),
        finding.RuleVersion,
        finding.SourceType,
        finding.SourceId,
        finding.ObservedValue,
        finding.ExpectedValue,
        finding.Threshold,
        finding.Explanation,
        finding.ObservedAtUtc,
        finding.FirstDetectedAtUtc,
        finding.AssignedToUserId,
        finding.AssignedTeamCode,
        finding.SuppressionExpiresAtUtc,
        finding.Revision);

    private static AnomalyFindingDto ToFindingDto(
        AnomalyFindingEntity finding,
        IReadOnlyList<AnomalyFindingObservationDto> observations,
        IReadOnlyList<AnomalyFindingHistoryDto> history,
        DateTimeOffset nowUtc) => new(
        finding.Fingerprint,
        finding.WarehouseId,
        Parse<AnomalyRuleKind>(finding.RuleKind),
        Parse<AnomalySeverity>(finding.Severity),
        Parse<AnomalyStatus>(finding.Status),
        finding.RuleVersion,
        finding.SourceType,
        finding.SourceId,
        finding.SourceWindowFromUtc,
        finding.SourceWindowToUtc,
        finding.ObservedValue,
        finding.ExpectedValue,
        finding.Threshold,
        finding.Explanation,
        finding.ObservedAtUtc,
        finding.FirstDetectedAtUtc,
        finding.AssignedToUserId,
        finding.AssignedTeamCode,
        finding.SuppressionExpiresAtUtc,
        finding.Status == nameof(AnomalyStatus.Suppressed) && finding.SuppressionExpiresAtUtc <= nowUtc,
        finding.Revision,
        observations,
        history);

    private static AnomalyRuleSettingDto ToRuleDto(AnomalyRuleConfigurationEntity rule) => new(
        rule.Id,
        Parse<AnomalyRuleKind>(rule.RuleKind),
        rule.WarehouseId,
        rule.Version,
        rule.Threshold,
        rule.IsEnabled,
        rule.UseExternalNotifications,
        rule.CreatedByUserId,
        rule.CreatedAtUtc);

    private static AnomalyDetectionRunDto ToRunDto(AnomalyDetectionRunEntity run, bool wasReused) => new(
        run.Id,
        run.WarehouseId,
        run.SourceWindowFromUtc,
        run.SourceWindowToUtc,
        run.InputFingerprint,
        run.SourceSignals,
        run.FindingsCreated,
        run.FindingsReused,
        JsonSerializer.Deserialize<string[]>(run.DataQualityFlagsJson, JsonOptions) ?? [],
        run.StartedAtUtc,
        run.CompletedAtUtc,
        wasReused);

    private static string WarehouseScopeKey(int warehouseId) => $"warehouse:{warehouseId}";

    private static string SourceKey(AnomalySignal signal) =>
        string.Join('|', signal.WarehouseId, signal.RuleKind, signal.ReferenceType, signal.ReferenceId);

    private static string SourceKey(AnomalyFindingEntity finding) =>
        string.Join('|', finding.WarehouseId, finding.RuleKind, finding.SourceType, finding.SourceId);

    private async Task<bool> CanReadSourceAsync(
        AnomalyRuleKind ruleKind,
        CancellationToken cancellationToken)
    {
        var permissions = ruleKind switch
        {
            AnomalyRuleKind.InventoryAdjustment or AnomalyRuleKind.ReversalBurst or AnomalyRuleKind.NegativeBalance =>
                new[] { WmsPermissions.InventoryRead },
            AnomalyRuleKind.CountVariance => new[] { WmsPermissions.CountingExecute },
            AnomalyRuleKind.DuplicateScan or AnomalyRuleKind.ReceivingDiscrepancy =>
                new[] { WmsPermissions.ReceiptsRead, WmsPermissions.ReceivingExecute },
            AnomalyRuleKind.ShippingDiscrepancy =>
                new[] { WmsPermissions.SalesOrdersRead, WmsPermissions.ShippingExecute },
            AnomalyRuleKind.AgeingWork => new[] { WmsPermissions.WorkRead },
            AnomalyRuleKind.IntegrationFailure => new[] { WmsPermissions.ReportsRead },
            _ => Array.Empty<string>()
        };

        foreach (var permission in permissions)
        {
            if (await warehouseAccessService.HasPermissionAsync(permission, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset UtcDayStart(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero);
    }

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : null;
    }

    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"Stored anomaly enum value '{value}' is invalid.");

    private static T? ParseNullable<T>(string? value) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Parse<T>(value);

    private sealed record SignalLoadResult(IReadOnlyList<AnomalySignal> Signals, IReadOnlyList<string> Flags);

    private sealed class SessionScanComparer : IEqualityComparer<(int ReceivingSessionId, string RawScanValue)>
    {
        public bool Equals((int ReceivingSessionId, string RawScanValue) x,
            (int ReceivingSessionId, string RawScanValue) y) =>
            x.ReceivingSessionId == y.ReceivingSessionId &&
            string.Equals(x.RawScanValue, y.RawScanValue, StringComparison.Ordinal);

        public int GetHashCode((int ReceivingSessionId, string RawScanValue) value) =>
            HashCode.Combine(value.ReceivingSessionId, StringComparer.Ordinal.GetHashCode(value.RawScanValue));
    }
}
