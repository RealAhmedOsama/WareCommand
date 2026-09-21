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
    IApiClientContextAccessor apiClientContext) : IWarehouseAccessService
{
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

        var userId = await GetActiveUserIdAsync(cancellationToken);
        if (userId is null)
        {
            return Result.Failure(WmsErrors.Unauthorized(
                "authorization.authentication_required",
                "An active authenticated user is required."));
        }

        if (!await HasPermissionClaimAsync(userId, permission, cancellationToken))
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

        var warehouseExists = await context.Warehouses
            .AsNoTracking()
            .AnyAsync(warehouse => warehouse.Id == warehouseId.Value && warehouse.IsActive, cancellationToken);
        if (!warehouseExists)
        {
            LogDenied(userId, permission, warehouseId, "warehouse-not-found");
            return Result.Failure(WmsErrors.NotFound(
                "warehouse.not_found",
                "The requested warehouse was not found or is inactive."));
        }

        var scope = await GetScopeAsync(cancellationToken);
        if (scope.HasGlobalAccess || scope.WarehouseIds.Contains(warehouseId.Value))
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

        var userId = await GetActiveUserIdAsync(cancellationToken);
        if (userId is null)
        {
            return WarehouseAccessScope.None;
        }

        if (await HasPermissionClaimAsync(userId, WmsPermissions.All, cancellationToken))
        {
            return new WarehouseAccessScope(true, new HashSet<int>());
        }

        var warehouseIds = await context.UserWarehouseAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.Warehouse.IsActive)
            .Select(assignment => assignment.WarehouseId)
            .ToHashSetAsync(cancellationToken);

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
                    false))
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
                defaultWarehouseIds.Contains(warehouse.Id)))
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
