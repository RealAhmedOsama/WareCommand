using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Wms.Application.Identity;

namespace Wms.ASP.Identity;

public sealed record WmsPermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class WmsPermissionAuthorizationHandler(
    IWarehouseAccessService warehouseAccessService,
    ILogger<WmsPermissionAuthorizationHandler> logger)
    : AuthorizationHandler<WmsPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WmsPermissionRequirement requirement)
    {
        if (await warehouseAccessService.HasPermissionAsync(requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        logger.LogWarning(
            "Authorization denied for user {UserId}; permission {Permission}",
            userId,
            requirement.Permission);
    }
}
