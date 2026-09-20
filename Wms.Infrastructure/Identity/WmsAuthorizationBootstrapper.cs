using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Wms.Application.Identity;

namespace Wms.Infrastructure.Identity;

public sealed class WmsAuthorizationBootstrapper(
    RoleManager<IdentityRole> roleManager)
{
    public async Task EnsureRolesAndPermissionsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var roleDefinition in WmsRolePermissionCatalog.Defaults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await roleManager.FindByNameAsync(roleDefinition.Key);
            if (role is null)
            {
                role = new IdentityRole(roleDefinition.Key);
                var roleResult = await roleManager.CreateAsync(role);
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not create authorization role '{roleDefinition.Key}': " +
                        string.Join("; ", roleResult.Errors.Select(error => error.Description)));
                }
            }

            var existingClaims = await roleManager.GetClaimsAsync(role);
            foreach (var permission in roleDefinition.Value)
            {
                if (existingClaims.Any(claim =>
                        claim.Type == WmsAuthorizationClaimTypes.Permission &&
                        claim.Value == permission))
                {
                    continue;
                }

                var claimResult = await roleManager.AddClaimAsync(
                    role,
                    new Claim(WmsAuthorizationClaimTypes.Permission, permission));
                if (!claimResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Could not assign permission '{permission}' to role '{roleDefinition.Key}': " +
                        string.Join("; ", claimResult.Errors.Select(error => error.Description)));
                }
            }
        }
    }
}
