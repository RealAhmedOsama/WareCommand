using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Application.Notifications;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.Notifications;

public sealed class NotificationRecipientDirectory(WmsDbContext context) : INotificationRecipientDirectory
{
    public async Task<IReadOnlyList<NotificationRecipientProfile>> ResolveAsync(
        NotificationAudience audience,
        string? requiredPermission,
        CancellationToken cancellationToken = default)
    {
        var userIds = audience.UserIds?
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Select(userId => userId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var roles = audience.Roles?
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        IQueryable<WmsUser> users = context.Users
            .AsNoTracking()
            .Where(user => user.IsActive);

        if (userIds.Length > 0)
        {
            users = users.Where(user => userIds.Contains(user.Id));
        }
        else if (roles.Length > 0)
        {
            users =
                from user in users
                join userRole in context.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
                join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where role.Name != null && roles.Contains(role.Name)
                select user;
        }
        else if (audience.WarehouseId.HasValue)
        {
            var warehouseId = audience.WarehouseId.Value;
            users = users.Where(user =>
                context.UserWarehouseAssignments.Any(assignment =>
                    assignment.UserId == user.Id &&
                    assignment.WarehouseId == warehouseId &&
                    assignment.Warehouse.IsActive) ||
                context.UserClaims.Any(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    claim.ClaimValue == WmsPermissions.All) ||
                context.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        context.RoleClaims,
                        userRole => userRole.RoleId,
                        roleClaim => roleClaim.RoleId,
                        (_, roleClaim) => roleClaim)
                    .Any(claim =>
                        claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                        claim.ClaimValue == WmsPermissions.All));
        }
        else
        {
            return [];
        }

        if (audience.WarehouseId.HasValue)
        {
            var warehouseId = audience.WarehouseId.Value;
            users = users.Where(user =>
                context.UserWarehouseAssignments.Any(assignment =>
                    assignment.UserId == user.Id &&
                    assignment.WarehouseId == warehouseId &&
                    assignment.Warehouse.IsActive) ||
                context.UserClaims.Any(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    claim.ClaimValue == WmsPermissions.All) ||
                context.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        context.RoleClaims,
                        userRole => userRole.RoleId,
                        roleClaim => roleClaim.RoleId,
                        (_, roleClaim) => roleClaim)
                    .Any(claim =>
                        claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                        claim.ClaimValue == WmsPermissions.All));
        }

        if (!string.IsNullOrWhiteSpace(requiredPermission))
        {
            var permission = requiredPermission.Trim();
            users = users.Where(user =>
                context.UserClaims.Any(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                    (claim.ClaimValue == permission || claim.ClaimValue == WmsPermissions.All)) ||
                context.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(
                        context.RoleClaims,
                        userRole => userRole.RoleId,
                        roleClaim => roleClaim.RoleId,
                        (_, roleClaim) => roleClaim)
                    .Any(claim =>
                        claim.ClaimType == WmsAuthorizationClaimTypes.Permission &&
                        (claim.ClaimValue == permission || claim.ClaimValue == WmsPermissions.All)));
        }

        var userRows = await users
            .Distinct()
            .OrderBy(user => user.Id)
            .ToListAsync(cancellationToken);
        if (userRows.Count == 0)
        {
            return [];
        }

        var resolvedUserIds = userRows.Select(user => user.Id).ToArray();
        var roleRows = await (
                from userRole in context.UserRoles.AsNoTracking()
                join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where resolvedUserIds.Contains(userRole.UserId) && role.Name != null
                select new { userRole.UserId, RoleName = role.Name! })
            .ToListAsync(cancellationToken);

        var rolesByUser = roleRows
            .GroupBy(row => row.UserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)new HashSet<string>(
                    group.Select(row => row.RoleName),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.Ordinal);

        return userRows
            .Select(user => new NotificationRecipientProfile(
                user.Id,
                user.UserName ?? user.Email ?? user.Id,
                user.Email,
                Wms.Application.Localization.WmsLocaleCatalog.Normalize(user.Locale),
                string.IsNullOrWhiteSpace(user.TimeZone) ? "UTC" : user.TimeZone,
                rolesByUser.TryGetValue(user.Id, out var userRoles)
                    ? userRoles
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

}
