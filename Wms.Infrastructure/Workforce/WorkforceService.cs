using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Time;
using Wms.Application.WarehouseWork;
using Wms.Application.Workforce;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Workforce;

public sealed class WorkforceService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService,
    IAuditWriter auditWriter,
    IClock clock,
    IWarehouseWorkService warehouseWorkService,
    IWarehouseWorkAssignmentEligibilityService eligibilityService) : IWorkforceService
{
    private static readonly WarehouseWorkStatus[] OpenStatuses =
    [
        WarehouseWorkStatus.Open,
        WarehouseWorkStatus.Available
    ];

    private static readonly WarehouseWorkStatus[] AssignedStatuses =
    [
        WarehouseWorkStatus.Assigned,
        WarehouseWorkStatus.InProgress,
        WarehouseWorkStatus.Paused
    ];

    public async Task<Result<WorkerProfileDto>> SaveWorkerProfileAsync(
        string userId,
        WorkerProfileInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WorkerProfileDto>();
        }

        try
        {
            var normalizedUserId = Required(userId, 450, nameof(userId));
            var userExists = await context.Users.AnyAsync(
                user => user.Id == normalizedUserId && user.IsActive,
                cancellationToken);
            if (!userExists)
            {
                return Result.Failure<WorkerProfileDto>(WmsErrors.NotFound(
                    "workforce.worker_not_found",
                    "The worker account was not found or is inactive."));
            }

            var hasWarehouseAccess = await context.UserWarehouseAssignments.AnyAsync(
                assignment => assignment.UserId == normalizedUserId &&
                              assignment.WarehouseId == input.WarehouseId,
                cancellationToken);
            if (!hasWarehouseAccess)
            {
                return Result.Failure<WorkerProfileDto>(WmsErrors.Validation(
                    "workforce.worker_warehouse_access_required",
                    "The worker must be assigned to the warehouse before a profile can be saved."));
            }

            var timeZone = NormalizeTimeZone(input.TimeZoneId);
            var skills = NormalizeCodes(input.SkillCodes, "SkillCodes");
            var certifications = NormalizeCodes(input.CertificationCodes, "CertificationCodes");
            var preferredZones = NormalizeIds(input.PreferredZoneLocationIds, "PreferredZoneLocationIds");
            var invalidZones = await context.Locations
                .Where(location => location.WarehouseId == input.WarehouseId &&
                                   preferredZones.Contains(location.Id))
                .Select(location => location.Id)
                .ToListAsync(cancellationToken);
            if (invalidZones.Count != preferredZones.Count)
            {
                return Result.Failure<WorkerProfileDto>(WmsErrors.Validation(
                    "workforce.preferred_zone_invalid",
                    "Every preferred zone must belong to the selected warehouse."));
            }

            var profile = await context.WarehouseWorkerProfiles.SingleOrDefaultAsync(
                candidate => candidate.UserId == normalizedUserId &&
                             candidate.WarehouseId == input.WarehouseId,
                cancellationToken);
            var skillJson = JsonSerializer.Serialize(skills);
            var certificationJson = JsonSerializer.Serialize(certifications);
            var preferredZoneJson = JsonSerializer.Serialize(preferredZones);
            if (profile is null)
            {
                profile = new WarehouseWorkerProfile(
                    normalizedUserId,
                    input.WarehouseId,
                    input.TeamCode,
                    input.ShiftCode,
                    input.ShiftStartAtUtc,
                    input.ShiftEndAtUtc,
                    timeZone,
                    skillJson,
                    certificationJson,
                    preferredZoneJson);
                if (!input.IsActive)
                {
                    profile.SetActive(false);
                }

                context.WarehouseWorkerProfiles.Add(profile);
            }
            else
            {
                profile.Update(
                    input.TeamCode,
                    input.ShiftCode,
                    input.ShiftStartAtUtc,
                    input.ShiftEndAtUtc,
                    timeZone,
                    skillJson,
                    certificationJson,
                    preferredZoneJson,
                    input.IsActive);
            }

            await context.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseWorkerProfileChanged,
                    WmsAuditEntityTypes.WarehouseWorkerProfile,
                    $"{profile.UserId}:{profile.WarehouseId}",
                    profile.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["profileId"] = profile.Id,
                        ["teamCode"] = profile.TeamCode,
                        ["shiftCode"] = profile.ShiftCode,
                        ["skillCount"] = skills.Count,
                        ["certificationCount"] = certifications.Count,
                        ["preferredZoneCount"] = preferredZones.Count,
                        ["isActive"] = profile.IsActive
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            return Result.Success(Map(profile));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WorkerProfileDto>(WmsErrors.Validation(
                "workforce.worker_profile_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Result.Failure<WorkerProfileDto>(WmsErrors.FromException(
                exception,
                "workforce.worker_profile_conflict",
                "The worker profile could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<WorkerProfileDto>>> ListWorkerProfilesAsync(
        int warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<WorkerProfileDto>>();
        }

        var query = context.WarehouseWorkerProfiles
            .AsNoTracking()
            .Where(profile => profile.WarehouseId == warehouseId);
        if (!includeInactive)
        {
            query = query.Where(profile => profile.IsActive);
        }

        var profiles = await query
            .OrderBy(profile => profile.TeamCode)
            .ThenBy(profile => profile.UserId)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<WorkerProfileDto>>(profiles.Select(Map).ToArray());
    }

    public async Task<Result<WorkQueueDto>> SaveQueueAsync(
        int? queueId,
        WorkQueueInput input,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkManage,
            input.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WorkQueueDto>();
        }

        try
        {
            var warehouseExists = await context.Warehouses.AnyAsync(
                warehouse => warehouse.Id == input.WarehouseId && warehouse.IsActive,
                cancellationToken);
            if (!warehouseExists)
            {
                return Result.Failure<WorkQueueDto>(WmsErrors.NotFound(
                    "workforce.warehouse_not_found",
                    "The queue warehouse was not found or is inactive."));
            }

            if (input.ZoneLocationId.HasValue && !await context.Locations.AnyAsync(
                    location => location.Id == input.ZoneLocationId.Value &&
                                location.WarehouseId == input.WarehouseId &&
                                location.IsActive,
                    cancellationToken))
            {
                return Result.Failure<WorkQueueDto>(WmsErrors.Validation(
                    "workforce.queue_zone_invalid",
                    "The queue zone must belong to the selected warehouse."));
            }

            var code = Required(input.Code, 50, nameof(input.Code)).ToUpperInvariant();
            var duplicate = await context.WarehouseWorkQueues.AnyAsync(
                queue => queue.WarehouseId == input.WarehouseId &&
                         queue.Code == code &&
                         (!queueId.HasValue || queue.Id != queueId.Value),
                cancellationToken);
            if (duplicate)
            {
                return Result.Failure<WorkQueueDto>(WmsErrors.Conflict(
                    "workforce.queue_code_conflict",
                    "The work queue code is already used in this warehouse."));
            }

            var skills = NormalizeCodes(input.RequiredSkillCodes, "RequiredSkillCodes");
            var certifications = NormalizeCodes(input.RequiredCertificationCodes, "RequiredCertificationCodes");
            var skillJson = JsonSerializer.Serialize(skills);
            var certificationJson = JsonSerializer.Serialize(certifications);
            WarehouseWorkQueue queue;
            if (queueId.HasValue)
            {
                queue = await context.WarehouseWorkQueues.SingleOrDefaultAsync(
                    candidate => candidate.Id == queueId.Value &&
                                 candidate.WarehouseId == input.WarehouseId,
                    cancellationToken)
                    ?? throw new KeyNotFoundException($"Queue '{queueId.Value}' was not found.");
                queue.Update(
                    input.Name,
                    input.WorkType,
                    input.ZoneLocationId,
                    input.Priority,
                    input.Capacity,
                    input.RequiredTeamCode,
                    input.AssignmentStrategy,
                    skillJson,
                    certificationJson,
                    input.IsActive);
            }
            else
            {
                queue = new WarehouseWorkQueue(
                    input.WarehouseId,
                    code,
                    input.Name,
                    input.WorkType,
                    input.ZoneLocationId,
                    input.Priority,
                    input.Capacity,
                    input.RequiredTeamCode,
                    input.AssignmentStrategy,
                    skillJson,
                    certificationJson);
                if (!input.IsActive)
                {
                    queue.SetActive(false);
                }

                context.WarehouseWorkQueues.Add(queue);
            }

            await context.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseWorkQueueChanged,
                    WmsAuditEntityTypes.WarehouseWorkQueue,
                    queue.Code,
                    queue.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["queueId"] = queue.Id,
                        ["workType"] = queue.WorkType.ToString(),
                        ["zoneLocationId"] = queue.ZoneLocationId,
                        ["capacity"] = queue.Capacity,
                        ["assignmentStrategy"] = queue.AssignmentStrategy.ToString(),
                        ["isActive"] = queue.IsActive
                    },
                    ActorUserId: actorUserId),
                cancellationToken);
            return Result.Success(Map(queue));
        }
        catch (KeyNotFoundException exception)
        {
            return Result.Failure<WorkQueueDto>(WmsErrors.NotFound(
                "workforce.queue_not_found",
                exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WorkQueueDto>(WmsErrors.Validation(
                "workforce.queue_invalid",
                exception.Message));
        }
        catch (DbUpdateException exception)
        {
            return Result.Failure<WorkQueueDto>(WmsErrors.FromException(
                exception,
                "workforce.queue_conflict",
                "The work queue could not be saved."));
        }
    }

    public async Task<Result<IReadOnlyList<WorkQueueDto>>> ListQueuesAsync(
        int warehouseId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            warehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<IReadOnlyList<WorkQueueDto>>();
        }

        var query = context.WarehouseWorkQueues
            .AsNoTracking()
            .Where(queue => queue.WarehouseId == warehouseId);
        if (!includeInactive)
        {
            query = query.Where(queue => queue.IsActive);
        }

        var queues = await query
            .OrderByDescending(queue => queue.Priority)
            .ThenBy(queue => queue.Code)
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<WorkQueueDto>>(queues.Select(Map).ToArray());
    }

    public async Task<Result<WorkforceSuggestionsDto>> SuggestAsync(
        WorkforceSuggestionsQuery query,
        string workerUserId,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WorkforceSuggestionsDto>();
        }

        var limit = Math.Clamp(query.Limit, 1, 200);
        var workQuery = context.WarehouseWorks
            .AsNoTracking()
            .Include(work => work.Lines)
            .Where(work => work.WarehouseId == query.WarehouseId &&
                           work.Status == WarehouseWorkStatus.Available &&
                           work.AssignedUserId == null &&
                           (!query.WorkType.HasValue || work.Type == query.WorkType.Value));
        if (!string.IsNullOrWhiteSpace(query.QueueCode))
        {
            var queueCode = query.QueueCode.Trim().ToUpperInvariant();
            workQuery = workQuery.Where(work => work.QueueCode == queueCode);
        }

        var work = await workQuery
            .OrderByDescending(value => value.Priority)
            .ThenBy(value => value.DueAtUtc)
            .ThenBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Take(limit * 3)
            .ToListAsync(cancellationToken);
        var queueCodes = work.Select(value => value.QueueCode).Distinct().ToArray();
        var queues = await context.WarehouseWorkQueues
            .AsNoTracking()
            .Where(queue => queue.WarehouseId == query.WarehouseId && queueCodes.Contains(queue.Code))
            .ToDictionaryAsync(queue => queue.Code, cancellationToken);

        var suggestions = new List<WorkSuggestionDto>(limit);
        foreach (var candidate in work)
        {
            if (suggestions.Count >= limit)
            {
                break;
            }

            var eligibility = await eligibilityService.ValidateAsync(
                candidate.Id,
                workerUserId,
                null,
                supervisorOverride: false,
                selfClaim: true,
                cancellationToken);
            if (eligibility.IsFailure)
            {
                continue;
            }

            queues.TryGetValue(candidate.QueueCode, out var queue);
            suggestions.Add(new WorkSuggestionDto(
                Map(candidate),
                queue is null
                    ? "Default work routing matched the worker's active warehouse profile."
                    : $"Queue '{queue.Code}' matched the worker's shift, skills, certifications, and zone eligibility.",
                queue?.Id,
                candidate.QueueCode,
                queue?.ZoneLocationId));
        }

        return Result.Success(new WorkforceSuggestionsDto(
            suggestions,
            NormalizeUtc(clock.UtcNow.UtcDateTime)));
    }

    public Task<Result<WarehouseWorkDto>> ClaimAsync(
        int workId,
        WarehouseWorkClaimInput input,
        string workerUserId,
        CancellationToken cancellationToken = default) =>
        warehouseWorkService.ClaimAsync(workId, input, workerUserId, cancellationToken);

    public async Task<Result<WorkforceActivityDto>> RecordActivityAsync(
        WorkforceActivityInput input,
        string workerUserId,
        CancellationToken cancellationToken = default)
    {
        var work = await context.WarehouseWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == input.WorkId, cancellationToken);
        if (work is null)
        {
            return Result.Failure<WorkforceActivityDto>(WmsErrors.NotFound(
                "workforce.work_not_found",
                "The warehouse work item was not found."));
        }

        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.WorkExecute,
            work.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WorkforceActivityDto>();
        }

        if (!string.Equals(work.AssignedUserId, workerUserId.Trim(), StringComparison.Ordinal))
        {
            return Result.Failure<WorkforceActivityDto>(WmsErrors.Forbidden(
                "workforce.activity_worker_mismatch",
                "Only the assigned worker can record activity for this work."));
        }

        try
        {
            var start = NormalizeUtc(input.StartedAtUtc);
            DateTime? end = input.EndedAtUtc.HasValue ? NormalizeUtc(input.EndedAtUtc.Value) : null;
            var existing = await context.WarehouseWorkActivities.SingleOrDefaultAsync(
                activity => activity.WarehouseWorkId == input.WorkId &&
                            activity.WorkerUserId == workerUserId.Trim() &&
                            activity.Category == input.Category &&
                            activity.StartedAtUtc == start,
                cancellationToken);
            if (existing is not null)
            {
                return Result.Success(Map(existing));
            }

            var activity = new WarehouseWorkActivity(
                work.WarehouseId,
                work.Id,
                workerUserId,
                input.Category,
                start,
                end,
                input.Reason);
            context.WarehouseWorkActivities.Add(activity);
            await context.SaveChangesAsync(cancellationToken);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.WarehouseWorkActivityRecorded,
                    WmsAuditEntityTypes.WarehouseWorkActivity,
                    activity.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    activity.WarehouseId,
                    After: new Dictionary<string, object?>
                    {
                        ["workId"] = activity.WarehouseWorkId,
                        ["category"] = activity.Category.ToString(),
                        ["startedAtUtc"] = activity.StartedAtUtc,
                        ["endedAtUtc"] = activity.EndedAtUtc
                    },
                    ActorUserId: workerUserId),
                cancellationToken);
            return Result.Success(Map(activity));
        }
        catch (ArgumentException exception)
        {
            return Result.Failure<WorkforceActivityDto>(WmsErrors.Validation(
                "workforce.activity_invalid",
                exception.Message));
        }
    }

    public async Task<Result<WorkforceMetricsDto>> GetMetricsAsync(
        WorkforceMetricsQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.ReportsRead,
            query.WarehouseId,
            cancellationToken);
        if (authorization.IsFailure)
        {
            return authorization.ToFailure<WorkforceMetricsDto>();
        }

        var toUtc = NormalizeUtc(query.ToUtc ?? clock.UtcNow.UtcDateTime);
        var fromUtc = NormalizeUtc(query.FromUtc ?? toUtc.AddDays(-1));
        if (toUtc <= fromUtc)
        {
            return Result.Failure<WorkforceMetricsDto>(WmsErrors.Validation(
                "workforce.metrics_range_invalid",
                "The metrics end must be later than the start."));
        }

        var workQuery = context.WarehouseWorks
            .AsNoTracking()
            .Where(work => work.WarehouseId == query.WarehouseId &&
                           work.CreatedAt < toUtc &&
                           (work.CompletedAtUtc == null || work.CompletedAtUtc >= fromUtc));
        if (!string.IsNullOrWhiteSpace(query.QueueCode))
        {
            var queueCode = query.QueueCode.Trim().ToUpperInvariant();
            workQuery = workQuery.Where(work => work.QueueCode == queueCode);
        }

        if (!string.IsNullOrWhiteSpace(query.WorkerUserId))
        {
            var workerUserId = query.WorkerUserId.Trim();
            workQuery = workQuery.Where(work => work.AssignedUserId == workerUserId ||
                                               work.CompletedByUserId == workerUserId);
        }

        var openCount = await workQuery.CountAsync(
            work => OpenStatuses.Contains(work.Status),
            cancellationToken);
        var assignedCount = await workQuery.CountAsync(
            work => AssignedStatuses.Contains(work.Status),
            cancellationToken);
        var completedCount = await workQuery.CountAsync(
            work => work.Status == WarehouseWorkStatus.Completed &&
                    work.CompletedAtUtc >= fromUtc && work.CompletedAtUtc < toUtc,
            cancellationToken);
        var exceptionCount = await workQuery.CountAsync(
            work => work.ExceptionAtUtc >= fromUtc && work.ExceptionAtUtc < toUtc,
            cancellationToken);

        var completedWorkQuery = workQuery.Where(
            work => work.Status == WarehouseWorkStatus.Completed &&
                    work.CompletedAtUtc >= fromUtc &&
                    work.CompletedAtUtc < toUtc);
        var completedLineCount = await (
            from line in context.WarehouseWorkLines.AsNoTracking()
            join work in completedWorkQuery on line.WarehouseWorkId equals work.Id
            select line.Id).CountAsync(cancellationToken);
        var completedUnits = await (
            from line in context.WarehouseWorkLines.AsNoTracking()
            join work in completedWorkQuery on line.WarehouseWorkId equals work.Id
            select (decimal?)line.ActualQuantity).SumAsync(cancellationToken) ?? 0m;

        var completedDurations = await completedWorkQuery
            .Where(work => work.StartedAtUtc.HasValue && work.CompletedAtUtc.HasValue)
            .Select(work => new { work.QueueCode, work.StartedAtUtc, work.CompletedAtUtc })
            .ToListAsync(cancellationToken);
        var averageActiveMinutes = completedDurations.Count == 0
            ? null
            : (double?)completedDurations.Average(value =>
                (value.CompletedAtUtc!.Value - value.StartedAtUtc!.Value).TotalMinutes);

        var grouped = await workQuery
            .GroupBy(work => work.QueueCode)
            .Select(group => new
            {
                QueueCode = group.Key,
                OpenCount = group.Count(work => OpenStatuses.Contains(work.Status)),
                AssignedCount = group.Count(work => AssignedStatuses.Contains(work.Status)),
                CompletedCount = group.Count(work => work.Status == WarehouseWorkStatus.Completed && work.CompletedAtUtc >= fromUtc && work.CompletedAtUtc < toUtc),
                ExceptionCount = group.Count(work => work.ExceptionAtUtc >= fromUtc && work.ExceptionAtUtc < toUtc)
            })
            .OrderByDescending(group => group.OpenCount + group.AssignedCount)
            .ThenBy(group => group.QueueCode)
            .ToListAsync(cancellationToken);
        var lineBuckets = await (
            from line in context.WarehouseWorkLines.AsNoTracking()
            join work in completedWorkQuery on line.WarehouseWorkId equals work.Id
            group line by work.QueueCode into groupedLines
            select new
            {
                QueueCode = groupedLines.Key,
                CompletedLines = groupedLines.Count(),
                CompletedUnits = groupedLines.Sum(line => line.ActualQuantity)
            }).ToListAsync(cancellationToken);
        var durationBuckets = completedDurations
            .GroupBy(value => value.QueueCode)
            .ToDictionary(
                group => group.Key,
                group => group.Average(value =>
                    (value.CompletedAtUtc!.Value - value.StartedAtUtc!.Value).TotalMinutes),
                StringComparer.Ordinal);
        var queueZones = await context.WarehouseWorkQueues
            .AsNoTracking()
            .Where(queue => queue.WarehouseId == query.WarehouseId)
            .ToDictionaryAsync(queue => queue.Code, queue => queue.ZoneLocationId, cancellationToken);
        var lineByQueue = lineBuckets.ToDictionary(value => value.QueueCode, StringComparer.Ordinal);
        var buckets = grouped.Select(group =>
        {
            lineByQueue.TryGetValue(group.QueueCode, out var lines);
            durationBuckets.TryGetValue(group.QueueCode, out var duration);
            return new WorkforceMetricBucketDto(
                group.QueueCode,
                queueZones.TryGetValue(group.QueueCode, out var zone) ? zone : null,
                group.OpenCount,
                group.AssignedCount,
                group.CompletedCount,
                group.ExceptionCount,
                lines?.CompletedLines ?? 0,
                lines?.CompletedUnits ?? 0m,
                duration == 0 ? null : duration,
                0,
                0,
                0);
        }).ToArray();

        var activityRows = await (
            from activity in context.WarehouseWorkActivities.AsNoTracking()
            join work in workQuery on activity.WarehouseWorkId equals work.Id
            where activity.StartedAtUtc < toUtc &&
                  (activity.EndedAtUtc == null || activity.EndedAtUtc > fromUtc)
            select new { work.QueueCode, activity.Category, activity.StartedAtUtc, activity.EndedAtUtc }
        ).ToListAsync(cancellationToken);
        var activityByQueue = activityRows
            .GroupBy(value => value.QueueCode)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Travel = DurationMinutes(group.Where(value => value.Category == WarehouseWorkActivityCategory.Travel), fromUtc, toUtc, value => value.StartedAtUtc, value => value.EndedAtUtc),
                    Wait = DurationMinutes(group.Where(value => value.Category == WarehouseWorkActivityCategory.Wait), fromUtc, toUtc, value => value.StartedAtUtc, value => value.EndedAtUtc),
                    Exception = DurationMinutes(group.Where(value => value.Category == WarehouseWorkActivityCategory.Exception), fromUtc, toUtc, value => value.StartedAtUtc, value => value.EndedAtUtc)
                },
                StringComparer.Ordinal);
        buckets = buckets.Select(bucket =>
        {
            activityByQueue.TryGetValue(bucket.QueueCode, out var activity);
            return bucket with
            {
                TravelMinutes = activity?.Travel ?? 0,
                WaitMinutes = activity?.Wait ?? 0,
                ExceptionMinutes = activity?.Exception ?? 0
            };
        }).ToArray();

        return Result.Success(new WorkforceMetricsDto(
            query.WarehouseId,
            fromUtc,
            toUtc,
            openCount,
            assignedCount,
            completedCount,
            exceptionCount,
            completedLineCount,
            completedUnits,
            averageActiveMinutes,
            buckets,
            "Metrics describe operational work facts and queue context; they are not punitive worker rankings."));
    }

    private static double DurationMinutes<T>(
        IEnumerable<T> activities,
        DateTime fromUtc,
        DateTime toUtc,
        Func<T, DateTime> startSelector,
        Func<T, DateTime?> endSelector) =>
        activities.Sum(activity =>
        {
            var activityStart = startSelector(activity);
            var activityEnd = endSelector(activity);
            var start = activityStart < fromUtc ? fromUtc : activityStart;
            var end = activityEnd.HasValue && activityEnd.Value < toUtc
                ? activityEnd.Value
                : toUtc;
            return end > start ? (end - start).TotalMinutes : 0d;
        });

    private static WorkerProfileDto Map(WarehouseWorkerProfile profile) => new(
        profile.Id,
        profile.UserId,
        profile.WarehouseId,
        profile.TeamCode,
        profile.ShiftCode,
        profile.ShiftStartAtUtc,
        profile.ShiftEndAtUtc,
        profile.TimeZoneId,
        ReadCodes(profile.SkillCodesJson),
        ReadCodes(profile.CertificationCodesJson),
        ReadIds(profile.PreferredZoneLocationIdsJson),
        profile.IsActive,
        profile.Revision);

    private static WorkQueueDto Map(WarehouseWorkQueue queue) => new(
        queue.Id,
        queue.WarehouseId,
        queue.Code,
        queue.Name,
        queue.WorkType,
        queue.ZoneLocationId,
        queue.Priority,
        queue.Capacity,
        queue.RequiredTeamCode,
        queue.AssignmentStrategy,
        ReadCodes(queue.RequiredSkillCodesJson),
        ReadCodes(queue.RequiredCertificationCodesJson),
        queue.IsActive,
        queue.Revision);

    private static WorkforceActivityDto Map(WarehouseWorkActivity activity) => new(
        activity.Id,
        activity.WarehouseWorkId,
        activity.WarehouseId,
        activity.WorkerUserId,
        activity.Category,
        activity.StartedAtUtc,
        activity.EndedAtUtc,
        activity.Reason,
        activity.Revision);

    private static WarehouseWorkDto Map(WarehouseWorkEntity work) => new(
        work.Id,
        work.WorkNumber,
        work.CreationKey,
        work.Type,
        work.WarehouseId,
        work.SourceEntityType,
        work.SourceEntityId,
        work.SourceLineReference,
        work.QueueCode,
        work.Priority,
        work.DueAtUtc,
        work.TeamCode,
        work.Notes,
        work.Status,
        work.AssignedUserId,
        work.AssignedTeamCode,
        work.AssignedByUserId,
        work.AssignedAtUtc,
        work.StartedAtUtc,
        work.PausedAtUtc,
        work.CompletedAtUtc,
        work.CancelledAtUtc,
        work.CompletedByUserId,
        work.CancelledByUserId,
        work.CancellationReason,
        work.ExceptionType,
        work.ExceptionReason,
        work.ExceptionAtUtc,
        work.SupervisorOverride,
        work.OverrideReason,
        work.Revision,
        work.Lines.OrderBy(line => line.Sequence).Select(line => new WarehouseWorkLineDto(
            line.Id,
            line.Sequence,
            line.WarehouseId,
            line.ItemId,
            line.PlannedQuantity,
            line.ActualQuantity,
            line.BaseUnitOfMeasure,
            line.SourceLocationId,
            line.DestinationLocationId,
            line.LotId,
            line.SerialNumberId,
            line.SerialNumber,
            line.LicensePlateId,
            line.InventoryStatusId,
            line.SourceReference,
            line.DimensionsSnapshot,
            line.Revision,
            line.ReservationId,
            line.ReservationAllocationId)).ToArray(),
        work.IsTerminal,
        work.HasExceptions,
        work.AllocationStrategyPolicyId,
        work.AllocationStrategyKey,
        work.AllocationStrategy,
        work.AllocationStrategyRevision);

    private static List<string> NormalizeCodes(IReadOnlyList<string>? values, string parameterName)
    {
        var normalized = (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count > 100 || normalized.Any(value => value.Length > 100))
        {
            throw new ArgumentException("A code list may contain at most 100 values of 100 characters each.", parameterName);
        }

        return normalized;
    }

    private static List<int> NormalizeIds(IReadOnlyList<int>? values, string parameterName)
    {
        var normalized = (values ?? []).Distinct().ToList();
        if (normalized.Any(value => value <= 0) || normalized.Count > 100)
        {
            throw new ArgumentException("An identifier list must contain at most 100 positive values.", parameterName);
        }

        return normalized;
    }

    private static string[] ReadCodes(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToUpperInvariant())
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int[] ReadIds(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<int[]>(json) ?? [])
                .Where(value => value > 0)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeTimeZone(string value) =>
        WmsTimeZoneCatalog.TryNormalize(value, out var normalized)
            ? normalized
            : throw new ArgumentException("The configured time zone is invalid.", nameof(value));

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

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
