using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Workforce;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Infrastructure.Data;
using WarehouseWorkEntity = Wms.Domain.Entities.WarehouseWork;

namespace Wms.Infrastructure.Workforce;

public sealed class WarehouseWorkAssignmentEligibilityService(
    WmsDbContext context,
    IClock clock) : IWarehouseWorkAssignmentEligibilityService
{
    private static readonly WarehouseWorkStatus[] CapacityStatuses =
    [
        WarehouseWorkStatus.Assigned,
        WarehouseWorkStatus.InProgress,
        WarehouseWorkStatus.Paused,
        WarehouseWorkStatus.Exception
    ];

    public async Task<Result> ValidateAsync(
        int workId,
        string? targetUserId,
        string? teamCode,
        bool supervisorOverride,
        bool selfClaim,
        CancellationToken cancellationToken = default)
    {
        if (supervisorOverride)
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(targetUserId) && string.IsNullOrWhiteSpace(teamCode))
        {
            return Result.Failure(WmsErrors.Validation(
                "work.assignment_target_required",
                "A worker or team is required for an assignment."));
        }

        var work = await context.WarehouseWorks
            .AsNoTracking()
            .Include(candidate => candidate.Lines)
            .SingleOrDefaultAsync(candidate => candidate.Id == workId, cancellationToken);
        if (work is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "work.not_found",
                "The warehouse work item was not found."));
        }

        var queue = await context.WarehouseWorkQueues
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.WarehouseId == work.WarehouseId &&
                             candidate.Code == work.QueueCode,
                cancellationToken);

        WarehouseWorkerProfile? profile = null;
        if (selfClaim || queue is not null && !string.IsNullOrWhiteSpace(targetUserId))
        {
            var profileResult = await LoadEligibleWorkerAsync(
                targetUserId,
                work.WarehouseId,
                cancellationToken);
            if (profileResult.IsFailure)
            {
                return Result.Failure(profileResult.Errors);
            }

            profile = profileResult.Value;
        }

        if (queue is null)
        {
            return selfClaim
                ? Result.Success()
                : Result.Success();
        }

        if (!queue.IsActive)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "work.queue_inactive",
                $"The work queue '{queue.Code}' is inactive."));
        }

        if (queue.WorkType != work.Type)
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "work.queue_type_mismatch",
                $"The work queue '{queue.Code}' does not accept {work.Type} work."));
        }

        if (selfClaim && queue.AssignmentStrategy == WarehouseWorkAssignmentStrategy.Manual)
        {
            return Result.Failure(WmsErrors.Forbidden(
                "work.self_claim_not_allowed",
                $"The work queue '{queue.Code}' requires supervisor assignment."));
        }

        var normalizedTeamCode = NormalizeOptional(teamCode);
        if (!string.IsNullOrWhiteSpace(queue.RequiredTeamCode) &&
            !string.Equals(queue.RequiredTeamCode, normalizedTeamCode ?? profile?.TeamCode, StringComparison.Ordinal))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "work.team_ineligible",
                $"The work queue '{queue.Code}' requires team '{queue.RequiredTeamCode}'."));
        }

        if (profile is not null)
        {
            var requiredSkills = ReadCodes(queue.RequiredSkillCodesJson);
            if (!requiredSkills.IsSubsetOf(ReadCodes(profile.SkillCodesJson)))
            {
                return Result.Failure(WmsErrors.Forbidden(
                    "work.skill_ineligible",
                    "The worker does not have all skills required by this queue."));
            }

            var requiredCertifications = ReadCodes(queue.RequiredCertificationCodesJson);
            if (!requiredCertifications.IsSubsetOf(ReadCodes(profile.CertificationCodesJson)))
            {
                return Result.Failure(WmsErrors.Forbidden(
                    "work.certification_ineligible",
                    "The worker does not have all certifications required by this queue."));
            }

            var preferredZones = ReadIds(profile.PreferredZoneLocationIdsJson);
            if (preferredZones.Count > 0 &&
                !await WorkMatchesZonesAsync(work, preferredZones, cancellationToken))
            {
                return Result.Failure(WmsErrors.Forbidden(
                    "work.preferred_zone_ineligible",
                    "The work is outside the worker's preferred zones."));
            }
        }

        if (queue.ZoneLocationId.HasValue &&
            !await WorkMatchesZonesAsync(work, [queue.ZoneLocationId.Value], cancellationToken))
        {
            return Result.Failure(WmsErrors.BusinessRule(
                "work.queue_zone_mismatch",
                $"The work does not belong to queue zone {queue.ZoneLocationId.Value}."));
        }

        if (queue.Capacity.HasValue)
        {
            var activeCount = await context.WarehouseWorks
                .AsNoTracking()
                .CountAsync(candidate => candidate.WarehouseId == work.WarehouseId &&
                                         candidate.QueueCode == queue.Code &&
                                         CapacityStatuses.Contains(candidate.Status),
                    cancellationToken);
            if (activeCount >= queue.Capacity.Value)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "work.queue_capacity_reached",
                    $"The work queue '{queue.Code}' is at capacity."));
            }
        }

        return Result.Success();
    }

    private async Task<Result<WarehouseWorkerProfile>> LoadEligibleWorkerAsync(
        string? userId,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Result.Failure<WarehouseWorkerProfile>(WmsErrors.Validation(
                "work.worker_required",
                "A worker is required for this assignment."));
        }

        var normalizedUserId = userId.Trim();
        var user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == normalizedUserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<WarehouseWorkerProfile>(WmsErrors.Forbidden(
                "work.worker_inactive",
                "The selected worker is not active."));
        }

        var hasWarehouseAccess = await context.UserWarehouseAssignments
            .AsNoTracking()
            .AnyAsync(assignment => assignment.UserId == normalizedUserId &&
                                   assignment.WarehouseId == warehouseId,
                cancellationToken);
        if (!hasWarehouseAccess)
        {
            return Result.Failure<WarehouseWorkerProfile>(WmsErrors.Forbidden(
                "work.worker_warehouse_access_required",
                "The selected worker is not assigned to this warehouse."));
        }

        var profile = await context.WarehouseWorkerProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.UserId == normalizedUserId &&
                                               candidate.WarehouseId == warehouseId,
                cancellationToken);
        if (profile is null || !profile.IsActive)
        {
            return Result.Failure<WarehouseWorkerProfile>(WmsErrors.Forbidden(
                "work.worker_profile_required",
                "The selected worker has no active warehouse profile."));
        }

        var now = clock.UtcNow.UtcDateTime;
        if (profile.ShiftStartAtUtc.HasValue &&
            profile.ShiftEndAtUtc.HasValue &&
            (now < profile.ShiftStartAtUtc.Value || now >= profile.ShiftEndAtUtc.Value))
        {
            return Result.Failure<WarehouseWorkerProfile>(WmsErrors.Forbidden(
                "work.shift_inactive",
                "The worker is outside the active shift window."));
        }

        return Result.Success(profile);
    }

    private async Task<bool> WorkMatchesZonesAsync(
        WarehouseWorkEntity work,
        IReadOnlyCollection<int> zoneIds,
        CancellationToken cancellationToken)
    {
        var locationIds = work.Lines
            .SelectMany(line => new[] { line.SourceLocationId, line.DestinationLocationId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (locationIds.Length == 0)
        {
            return false;
        }

        var parents = await context.Locations
            .AsNoTracking()
            .Where(location => location.WarehouseId == work.WarehouseId)
            .Select(location => new { location.Id, location.ParentLocationId })
            .ToDictionaryAsync(location => location.Id, location => location.ParentLocationId, cancellationToken);

        foreach (var locationId in locationIds)
        {
            var current = (int?)locationId;
            var visited = new HashSet<int>();
            while (current.HasValue && visited.Add(current.Value))
            {
                if (zoneIds.Contains(current.Value))
                {
                    return true;
                }

                current = parents.TryGetValue(current.Value, out var parentId)
                    ? parentId
                    : null;
            }
        }

        return false;
    }

    private static HashSet<string> ReadCodes(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static HashSet<int> ReadIds(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<int[]>(json) ?? [])
                .Where(value => value > 0)
                .ToHashSet();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
