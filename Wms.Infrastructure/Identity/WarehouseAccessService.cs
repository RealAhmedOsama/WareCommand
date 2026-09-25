using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.ApiClients;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identity;

public sealed class WarehouseAccessService(
    WmsDbContext context,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<WarehouseAccessService> logger,
    IApiClientContextAccessor apiClientContext) :
    IWarehouseAccessService,
    IWarehouseNavigationAccessService
{
    public async Task<WarehouseNavigationAccess> GetNavigationAccessAsync(
        CancellationToken cancellationToken = default)
    {
        if (apiClientContext.Current is { } apiClient)
        {
            IReadOnlyList<WmsWarehouseOption> apiWarehouses = [];
            if (apiClient.HasScope(WmsPermissions.DashboardView))
            {
                var apiWarehousesQuery = context.Warehouses
                    .AsNoTracking()
                    .Where(warehouse => warehouse.IsActive);
                if (!apiClient.HasGlobalWarehouseAccess)
                {
                    apiWarehousesQuery = apiWarehousesQuery
                        .Where(warehouse => apiClient.WarehouseIds.Contains(warehouse.Id));
                }

                apiWarehouses = await apiWarehousesQuery
                    .OrderBy(warehouse => warehouse.Code)
                    .Select(warehouse => new WmsWarehouseOption(
                        warehouse.Id,
                        warehouse.Code,
                        warehouse.Name,
                        false,
                        warehouse.TimeZone))
                    .ToListAsync(cancellationToken);
            }

            return new WarehouseNavigationAccess(
                new HashSet<string>(apiClient.Scopes, StringComparer.Ordinal),
                apiWarehouses,
                HasWildcardPermissions: false);
        }

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return WarehouseNavigationAccess.Empty;
        }

        var userId = currentUser.UserId;
        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Id, candidate.IsActive, candidate.LockoutEnd })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null ||
            !user.IsActive ||
            (user.LockoutEnd.HasValue && user.LockoutEnd > clock.UtcNow))
        {
            return WarehouseNavigationAccess.Empty;
        }

        var directPermissions = context.UserClaims
            .AsNoTracking()
            .Where(claim => claim.UserId == user.Id &&
                            claim.ClaimType == WmsAuthorizationClaimTypes.Permission)
            .Select(claim => claim.ClaimValue);
        var rolePermissions = context.RoleClaims
            .AsNoTracking()
            .Where(claim => claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                            context.UserRoles.Any(userRole =>
                                userRole.UserId == user.Id && userRole.RoleId == claim.RoleId))
            .Select(claim => claim.ClaimValue);
        var permissionValues = await directPermissions
                .Concat(rolePermissions)
                .Distinct()
                .ToListAsync(cancellationToken);
        var permissions = permissionValues
            .Where(permission => permission is not null)
            .Select(permission => permission!)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<WmsWarehouseOption> accessibleWarehouses = [];
        if (permissions.Contains(WmsPermissions.DashboardView) ||
            permissions.Contains(WmsPermissions.All))
        {
            var hasGlobalAccess = permissions.Contains(WmsPermissions.All);
            var userWarehousesQuery = context.Warehouses
                .AsNoTracking()
                .Where(warehouse => warehouse.IsActive);
            if (!hasGlobalAccess)
            {
                userWarehousesQuery = userWarehousesQuery
                    .Where(warehouse => context.UserWarehouseAssignments
                        .Any(assignment => assignment.UserId == user.Id &&
                                           assignment.WarehouseId == warehouse.Id));
            }

            accessibleWarehouses = await userWarehousesQuery
                .OrderBy(warehouse => warehouse.Code)
                .Select(warehouse => new WmsWarehouseOption(
                    warehouse.Id,
                    warehouse.Code,
                    warehouse.Name,
                    context.UserWarehouseAssignments.Any(assignment =>
                        assignment.UserId == user.Id &&
                        assignment.WarehouseId == warehouse.Id &&
                        assignment.IsDefault),
                    warehouse.TimeZone))
                .ToListAsync(cancellationToken);
        }

        return new WarehouseNavigationAccess(
            permissions,
            accessibleWarehouses,
            HasWildcardPermissions: true);
    }

    public async Task<bool> HasPermissionAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        if (!IsPermissionValueAllowed(permission))
        {
            return false;
        }

        if (apiClientContext.Current is { } apiClient)
        {
            return apiClient.HasScope(permission);
        }

        var userId = await GetActiveUserIdAsync(cancellationToken);
        return userId is not null &&
               await HasPermissionClaimAsync(userId, permission, cancellationToken);
    }

    public async Task<Result> AuthorizeAsync(
        string permission,
        int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsPermissionValueAllowed(permission))
        {
            return Result.Failure(WmsErrors.Validation(
                "authorization.permission_invalid",
                "The requested permission is not recognized."));
        }

        if (apiClientContext.Current is { } apiClient)
        {
            if (!apiClient.HasScope(permission))
            {
                return Result.Failure(WmsErrors.Forbidden(
                    "authorization.permission_denied",
                    "The API client does not have the requested scope."));
            }

            if (!warehouseId.HasValue)
            {
                return Result.Success();
            }

            var apiWarehouseExists = await context.Warehouses
                .AsNoTracking()
                .AnyAsync(warehouse => warehouse.Id == warehouseId.Value && warehouse.IsActive, cancellationToken);
            if (!apiWarehouseExists)
            {
                return Result.Failure(WmsErrors.NotFound(
                    "warehouse.not_found",
                    "The requested warehouse was not found or is inactive."));
            }

            return apiClient.CanAccessWarehouse(warehouseId.Value)
                ? Result.Success()
                : Result.Failure(WmsErrors.Forbidden(
                    "authorization.warehouse_scope_denied",
                    "The API client is not assigned to the requested warehouse."));
        }

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return Result.Failure(WmsErrors.Unauthorized(
                "authorization.authentication_required",
                "An active authenticated user is required."));
        }

        var userId = currentUser.UserId;
        var nowUtc = clock.UtcNow;
        var snapshot = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new UserAuthorizationSnapshot(
                user.IsActive,
                user.LockoutEnd,
                context.UserClaims.Any(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    (claim.ClaimValue == permission || claim.ClaimValue == WmsPermissions.All)) ||
                context.RoleClaims.Any(claim =>
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    (claim.ClaimValue == permission || claim.ClaimValue == WmsPermissions.All) &&
                    context.UserRoles.Any(userRole =>
                        userRole.UserId == user.Id && userRole.RoleId == claim.RoleId)),
                context.UserClaims.Any(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    claim.ClaimValue == WmsPermissions.All) ||
                context.RoleClaims.Any(claim =>
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    claim.ClaimValue == WmsPermissions.All &&
                    context.UserRoles.Any(userRole =>
                        userRole.UserId == user.Id && userRole.RoleId == claim.RoleId)),
                !warehouseId.HasValue || context.Warehouses.Any(warehouse =>
                    warehouse.Id == warehouseId.Value && warehouse.IsActive),
                !warehouseId.HasValue || context.UserWarehouseAssignments.Any(assignment =>
                    assignment.UserId == user.Id &&
                    assignment.WarehouseId == warehouseId.Value &&
                    assignment.Warehouse.IsActive)))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot is null ||
            !snapshot.IsActive ||
            (snapshot.LockoutEnd.HasValue && snapshot.LockoutEnd > nowUtc))
        {
            return Result.Failure(WmsErrors.Unauthorized(
                "authorization.authentication_required",
                "An active authenticated user is required."));
        }

        if (!snapshot.HasPermission)
        {
            LogDenied(userId, permission, warehouseId, "permission");
            return Result.Failure(WmsErrors.Forbidden(
                "authorization.permission_denied",
                "You do not have permission to perform this operation."));
        }

        if (!warehouseId.HasValue)
        {
            return Result.Success();
        }

        if (!snapshot.WarehouseExists)
        {
            LogDenied(userId, permission, warehouseId, "warehouse-not-found");
            return Result.Failure(WmsErrors.NotFound(
                "warehouse.not_found",
                "The requested warehouse was not found or is inactive."));
        }

        if (snapshot.HasGlobalAccess || snapshot.HasWarehouseAssignment)
        {
            return Result.Success();
        }

        LogDenied(userId, permission, warehouseId, "warehouse-scope");
        return Result.Failure(WmsErrors.Forbidden(
            "authorization.warehouse_scope_denied",
            "You are not assigned to the requested warehouse."));
    }

    public async Task<WarehouseAccessScope> GetScopeAsync(
        CancellationToken cancellationToken = default)
    {
        if (apiClientContext.Current is { } apiClient)
        {
            return new WarehouseAccessScope(
                apiClient.HasGlobalWarehouseAccess,
                apiClient.WarehouseIds);
        }

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return WarehouseAccessScope.None;
        }

        var userId = currentUser.UserId;
        var nowUtc = clock.UtcNow;
        var snapshots = await (
            from user in context.Users.AsNoTracking()
            where user.Id == userId
            let hasGlobalAccess =
                context.UserClaims.Any(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    claim.ClaimValue == WmsPermissions.All) ||
                context.RoleClaims.Any(claim =>
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    claim.ClaimValue == WmsPermissions.All &&
                    context.UserRoles.Any(userRole =>
                        userRole.UserId == user.Id &&
                        userRole.RoleId == claim.RoleId))
            join assignment in context.UserWarehouseAssignments
                    .AsNoTracking()
                    .Where(value => value.Warehouse.IsActive)
                on user.Id equals assignment.UserId into assignments
            from assignment in assignments.DefaultIfEmpty()
            select new
            {
                user.IsActive,
                user.LockoutEnd,
                HasGlobalAccess = hasGlobalAccess,
                WarehouseId = assignment == null ? (int?)null : assignment.WarehouseId
            }).ToListAsync(cancellationToken);

        if (snapshots.Count == 0 ||
            !snapshots[0].IsActive ||
            (snapshots[0].LockoutEnd.HasValue && snapshots[0].LockoutEnd > nowUtc))
        {
            return WarehouseAccessScope.None;
        }

        if (snapshots[0].HasGlobalAccess)
        {
            return new WarehouseAccessScope(true, new HashSet<int>());
        }

        var warehouseIds = snapshots
            .Where(snapshot => snapshot.WarehouseId.HasValue)
            .Select(snapshot => snapshot.WarehouseId!.Value)
            .ToHashSet();

        return new WarehouseAccessScope(false, warehouseIds);
    }

    public async Task<IReadOnlyList<WmsWarehouseOption>> GetAccessibleWarehousesAsync(
        string permission,
        CancellationToken cancellationToken = default)
    {
        var authorization = await AuthorizeAsync(permission, cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return [];
        }

        if (apiClientContext.Current is { } apiClient)
        {
            var apiScope = await GetScopeAsync(cancellationToken);
            var apiWarehousesQuery = context.Warehouses
                .AsNoTracking()
                .Where(warehouse => warehouse.IsActive);
            if (!apiScope.HasGlobalAccess)
            {
                apiWarehousesQuery = apiWarehousesQuery
                    .Where(warehouse => apiScope.WarehouseIds.Contains(warehouse.Id));
            }

            return await apiWarehousesQuery
                .OrderBy(warehouse => warehouse.Code)
                .Select(warehouse => new WmsWarehouseOption(
                    warehouse.Id,
                    warehouse.Code,
                    warehouse.Name,
                    false,
                    warehouse.TimeZone))
                .ToListAsync(cancellationToken);
        }

        var userId = await GetActiveUserIdAsync(cancellationToken);
        if (userId is null)
        {
            return [];
        }

        var scope = await GetScopeAsync(cancellationToken);
        var warehousesQuery = context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouse.IsActive);

        if (!scope.HasGlobalAccess)
        {
            warehousesQuery = warehousesQuery
                .Where(warehouse => scope.WarehouseIds.Contains(warehouse.Id));
        }

        var defaultWarehouseIds = await context.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.IsDefault)
            .Select(assignment => assignment.WarehouseId)
            .ToHashSetAsync(cancellationToken);

        return await warehousesQuery
            .OrderBy(warehouse => warehouse.Code)
            .Select(warehouse => new WmsWarehouseOption(
                warehouse.Id,
                warehouse.Code,
                warehouse.Name,
                defaultWarehouseIds.Contains(warehouse.Id),
                warehouse.TimeZone))
            .ToListAsync(cancellationToken);
    }

    private async Task<string?> GetActiveUserIdAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return null;
        }

        var userId = currentUser.UserId;
        var user = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (user is null ||
            !user.IsActive ||
            (user.LockoutEnd.HasValue && user.LockoutEnd > clock.UtcNow))
        {
            return null;
        }

        return user.Id;
    }

    private async Task<bool> HasPermissionClaimAsync(
        string userId,
        string permission,
        CancellationToken cancellationToken)
    {
        var directPermission = await context.UserClaims
            .AsNoTracking()
            .AnyAsync(claim => claim.UserId == userId &&
                               claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                               (claim.ClaimValue == permission || claim.ClaimValue == WmsPermissions.All),
                cancellationToken);
        if (directPermission)
        {
            return true;
        }

        var roleIds = context.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.RoleId);

        return await context.RoleClaims
            .AsNoTracking()
            .AnyAsync(claim => roleIds.Contains(claim.RoleId) &&
                               claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                               (claim.ClaimValue == permission || claim.ClaimValue == WmsPermissions.All),
                cancellationToken);
    }

    private static bool IsPermissionValueAllowed(string permission) =>
        permission == WmsPermissions.All || WmsPermissions.IsKnown(permission);

    private sealed record UserAuthorizationSnapshot(
        bool IsActive,
        DateTimeOffset? LockoutEnd,
        bool HasPermission,
        bool HasGlobalAccess,
        bool WarehouseExists,
        bool HasWarehouseAssignment);

    private void LogDenied(string userId, string permission, int? warehouseId, string reason)
    {
        logger.LogWarning(
            "Authorization denied for user {UserId}; permission {Permission}; warehouse {WarehouseId}; reason {Reason}",
            userId,
            permission,
            warehouseId,
            reason);
    }
}
