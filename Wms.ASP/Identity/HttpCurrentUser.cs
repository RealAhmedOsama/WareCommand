using System.Security.Claims;
using Wms.Application.Identity;

namespace Wms.ASP.Identity;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public string? UserId => Principal.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal.Identity?.Name;

    public string? DisplayName => Principal.FindFirstValue(WmsClaimTypes.DisplayName) ?? UserName;
}
