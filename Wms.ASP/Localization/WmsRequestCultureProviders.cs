using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Wms.Application.Localization;
using Wms.Application.Settings;
using Wms.Infrastructure.Identity;

namespace Wms.ASP.Localization;

public sealed class WmsUserRequestCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<WmsUser>>();
        var user = await userManager.FindByIdAsync(userId);
        return WmsLocaleCatalog.IsSupported(user?.Locale)
            ? new ProviderCultureResult(WmsLocaleCatalog.Normalize(user!.Locale))
            : null;
    }
}

public sealed class WmsSettingsRequestCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var settingsService = httpContext.RequestServices.GetRequiredService<IWmsSettingsService>();
        var result = await settingsService.GetAsync(cancellationToken: httpContext.RequestAborted);
        if (result.IsFailure || !WmsLocaleCatalog.IsSupported(result.Value.Values.Localization.Locale))
        {
            return null;
        }

        return new ProviderCultureResult(
            WmsLocaleCatalog.Normalize(result.Value.Values.Localization.Locale));
    }
}
