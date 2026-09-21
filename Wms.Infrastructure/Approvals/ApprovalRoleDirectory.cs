using Microsoft.EntityFrameworkCore;
using Wms.Application.Approvals;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Approvals;

public sealed class ApprovalRoleDirectory(WmsDbContext context) : IApprovalRoleDirectory
{
    public async Task<IReadOnlySet<string>> GetRolesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var roles = await (
                from userRole in context.UserRoles.AsNoTracking()
                join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where userRole.UserId == userId && role.Name != null
                select role.Name!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> FindUserIdsAsync(
        IReadOnlyCollection<string> roles,
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoles = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedRoles.Length == 0)
        {
            return [];
        }

        var users =
            from user in context.Users.AsNoTracking()
            join userRole in context.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
            join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where user.IsActive && role.Name != null && normalizedRoles.Contains(role.Name)
            select user;

        if (warehouseId.HasValue)
        {
            var warehouse = warehouseId.Value;
            users = users.Where(user =>
                context.UserWarehouseAssignments.Any(assignment =>
                    assignment.UserId == user.Id && assignment.WarehouseId == warehouse) ||
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

        return await users
            .Select(user => user.Id)
            .Distinct()
            .OrderBy(userId => userId)
            .ToListAsync(cancellationToken);
    }
}
