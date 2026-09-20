using System.Security.Claims;
using Wms.Application.Context;
using Wms.Application.Identity;

namespace Wms.ASP.Identity;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true ||
        WmsActorContext.Current is not null;

    public string? UserId => Principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
        WmsActorContext.Current?.UserId;

    public string? UserName => Principal.Identity?.Name ?? WmsActorContext.Current?.UserName;

    public string? DisplayName => Principal.FindFirstValue(WmsClaimTypes.DisplayName) ??
        UserName ??
        WmsActorContext.Current?.DisplayName;
}
