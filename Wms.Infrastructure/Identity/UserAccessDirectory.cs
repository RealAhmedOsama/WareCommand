using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identity;

public sealed class UserAccessDirectory(
    WmsDbContext context,
    UserManager<WmsUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IAuthenticationAuditService auditService,
    IAuditWriter auditWriter,
    IClock clock) : IUserAccessDirectory
{
    public async Task<WmsUserAccessProfile?> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var assignedRoleNames = (await userManager.GetRolesAsync(user))
            .ToHashSet(StringComparer.Ordinal);
        var directClaims = (await userManager.GetClaimsAsync(user))
            .Where(claim => claim.Type == WmsAuthorizationClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
        var rolePermissions = new HashSet<string>(StringComparer.Ordinal);

        foreach (var roleName in assignedRoleNames)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var roleClaims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in roleClaims.Where(claim => claim.Type == WmsAuthorizationClaimTypes.Permission))
            {
                rolePermissions.Add(claim.Value);
            }
        }

        var assignments = await context.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.Warehouse.IsActive)
            .ToDictionaryAsync(assignment => assignment.WarehouseId, cancellationToken);

        var warehouseRows = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .OrderBy(warehouse => warehouse.Code)
            .ToListAsync(cancellationToken);
        var warehouses = warehouseRows
            .Select(warehouse => new WmsAccessWarehouseOption(
                warehouse.Id,
                warehouse.Code,
                warehouse.Name,
                assignments.ContainsKey(warehouse.Id),
                assignments.TryGetValue(warehouse.Id, out var assignment) && assignment.IsDefault))
            .ToList();

        var roles = WmsRoleNames.Catalog
            .Select(roleName => new WmsAccessRoleOption(
                roleName,
                assignedRoleNames.Contains(roleName)))
            .ToList();
        var permissions = WmsPermissions.Catalog
            .Select(permission => new WmsAccessPermissionOption(
                permission,
                WmsPermissions.Descriptions[permission],
                directClaims.Contains(permission),
                rolePermissions.Contains(permission) || rolePermissions.Contains(WmsPermissions.All)))
            .ToList();

        return new WmsUserAccessProfile(
            user.Id,
            user.UserName ?? string.Empty,
            roles,
            permissions,
            warehouses);
    }

    public async Task<Result> UpdateAsync(
        string actorUserId,
        string targetUserId,
        IReadOnlyCollection<string> roleNames,
        IReadOnlyCollection<string> permissionNames,
        IReadOnlyCollection<int> warehouseIds,
        int? defaultWarehouseId,
        CancellationToken cancellationToken = default)
    {
        var actor = await userManager.FindByIdAsync(actorUserId);
        if (actor is null || !actor.IsActive ||
            !await userManager.IsInRoleAsync(actor, WmsRoleNames.Administrator))
        {
            return Result.Failure(WmsErrors.Forbidden(
                "access.administrator_required",
                "Only an active administrator can change access assignments."));
        }

        var target = await userManager.FindByIdAsync(targetUserId);
        if (target is null)
        {
            return Result.Failure(WmsErrors.NotFound(
                "account.not_found",
                "The selected account was not found."));
        }

        var normalizedRoles = roleNames
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var unknownRoles = normalizedRoles
            .Except(WmsRoleNames.Catalog, StringComparer.Ordinal)
            .ToArray();
        if (unknownRoles.Length > 0)
        {
            return Result.Failure(WmsErrors.Validation(
                "access.role_invalid",
                "One or more selected roles are not recognized."));
        }

        var normalizedPermissions = permissionNames
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedPermissions.Any(permission => !WmsPermissions.IsKnown(permission)))
        {
            return Result.Failure(WmsErrors.Validation(
                "access.permission_invalid",
                "One or more selected permissions are not recognized."));
        }

        var normalizedWarehouseIds = warehouseIds
            .Distinct()
            .ToArray();
        var activeWarehouseIds = await context.Warehouses
            .Where(warehouse => normalizedWarehouseIds.Contains(warehouse.Id) && warehouse.IsActive)
            .Select(warehouse => warehouse.Id)
            .ToHashSetAsync(cancellationToken);
        if (activeWarehouseIds.Count != normalizedWarehouseIds.Length)
        {
            return Result.Failure(WmsErrors.NotFound(
                "warehouse.selection_invalid",
                "One or more selected warehouses are missing or inactive."));
        }

        if (defaultWarehouseId.HasValue && !activeWarehouseIds.Contains(defaultWarehouseId.Value))
        {
            return Result.Failure(WmsErrors.Validation(
                "warehouse.default_invalid",
                "The default warehouse must be one of the assigned warehouses."));
        }

        var existingRoles = (await userManager.GetRolesAsync(target))
            .ToHashSet(StringComparer.Ordinal);
        var existingPermissionClaims = (await userManager.GetClaimsAsync(target))
            .Where(claim => claim.Type == WmsAuthorizationClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToArray();
        var existingAssignments = await context.UserWarehouseAssignments
            .Where(assignment => assignment.UserId == targetUserId)
            .ToListAsync(cancellationToken);
        var isRemovingAdministrator = existingRoles.Contains(WmsRoleNames.Administrator) &&
                                      !normalizedRoles.Contains(WmsRoleNames.Administrator, StringComparer.Ordinal);
        if (isRemovingAdministrator)
        {
            if (actorUserId == targetUserId)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "access.self_admin_removal",
                    "You cannot remove your own administrator access."));
            }

            var administratorRole = await roleManager.FindByNameAsync(WmsRoleNames.Administrator);
            if (administratorRole is null)
            {
                return Result.Failure(WmsErrors.Dependency(
                    "access.administrator_role_missing",
                    "The administrator role is not available.",
                    isRetryable: false));
            }

            var otherActiveAdministrators = await (
                from userRole in context.UserRoles
                join user in context.Users on userRole.UserId equals user.Id
                where userRole.RoleId == administratorRole.Id &&
                      user.Id != targetUserId &&
                      user.IsActive
                select user.Id).CountAsync(cancellationToken);
            if (otherActiveAdministrators == 0)
            {
                return Result.Failure(WmsErrors.Conflict(
                    "access.last_admin",
                    "At least one other active administrator must remain."));
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var removeRolesResult = await userManager.RemoveFromRolesAsync(target, existingRoles);
        if (!removeRolesResult.Succeeded)
        {
            return IdentityFailure("Could not remove the previous roles", removeRolesResult);
        }

        var addRolesResult = await userManager.AddToRolesAsync(target, normalizedRoles);
        if (!addRolesResult.Succeeded)
        {
            return IdentityFailure("Could not assign the selected roles", addRolesResult);
        }

        var existingPermissionClaimObjects = (await userManager.GetClaimsAsync(target))
            .Where(claim => claim.Type == WmsAuthorizationClaimTypes.Permission)
            .ToArray();
        if (existingPermissionClaimObjects.Length > 0)
        {
            var removeClaimsResult = await userManager.RemoveClaimsAsync(target, existingPermissionClaimObjects);
            if (!removeClaimsResult.Succeeded)
            {
                return IdentityFailure("Could not remove the previous permission grants", removeClaimsResult);
            }
        }

        if (normalizedPermissions.Length > 0)
        {
            var addClaimsResult = await userManager.AddClaimsAsync(
                target,
                normalizedPermissions.Select(permission =>
                    new Claim(WmsAuthorizationClaimTypes.Permission, permission)));
            if (!addClaimsResult.Succeeded)
            {
                return IdentityFailure("Could not assign the selected permissions", addClaimsResult);
            }
        }

        context.UserWarehouseAssignments.RemoveRange(existingAssignments);
        context.UserWarehouseAssignments.AddRange(normalizedWarehouseIds.Select(warehouseId =>
            new WmsUserWarehouseAssignment
            {
                UserId = targetUserId,
                WarehouseId = warehouseId,
                IsDefault = defaultWarehouseId == warehouseId,
                AssignedAtUtc = clock.UtcNow
            }));

        var stampResult = await userManager.UpdateSecurityStampAsync(target);
        if (!stampResult.Succeeded)
        {
            return IdentityFailure("Could not rotate the account security stamp", stampResult);
        }

        await auditWriter.RecordAsync(
            new AuditRecord(
                WmsAuditActions.AccessChanged,
                WmsAuditEntityTypes.AccessAssignment,
                targetUserId,
                Before: new Dictionary<string, object?>
                {
                    ["roles"] = existingRoles.ToArray(),
                    ["permissions"] = existingPermissionClaims,
                    ["warehouseIds"] = existingAssignments.Select(assignment => assignment.WarehouseId).ToArray(),
                    ["defaultWarehouseId"] = existingAssignments
                        .Where(assignment => assignment.IsDefault)
                        .Select(assignment => (int?)assignment.WarehouseId)
                        .FirstOrDefault()
                },
                After: new Dictionary<string, object?>
                {
                    ["roles"] = normalizedRoles,
                    ["permissions"] = normalizedPermissions,
                    ["warehouseIds"] = normalizedWarehouseIds,
                    ["defaultWarehouseId"] = defaultWarehouseId
                },
                ActorUserId: actorUserId,
                ActorUserName: actor.UserName,
                Details: "User roles, direct permissions, and warehouse assignments changed."),
            cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await auditService.RecordAsync(
            WmsAuthenticationEventTypes.AuthorizationChanged,
            succeeded: true,
            userId: targetUserId,
            userName: target.UserName,
            details: $"Access assignments changed by administrator {actorUserId}.",
            cancellationToken: cancellationToken);

        return Result.Success();
    }

    private static Result IdentityFailure(string prefix, IdentityResult result) =>
        Result.Failure(prefix + ": " + string.Join("; ", result.Errors.Select(error => error.Description)));
}
