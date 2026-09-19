using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Wms.ASP.Identity;

public sealed class WmsClaimsPrincipalFactory(
    UserManager<Wms.Infrastructure.Identity.WmsUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<Wms.Infrastructure.Identity.WmsUser, IdentityRole>(
        userManager,
        roleManager,
        optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(
        Wms.Infrastructure.Identity.WmsUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(WmsClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
