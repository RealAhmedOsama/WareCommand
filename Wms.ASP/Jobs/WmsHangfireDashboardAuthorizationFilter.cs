using Hangfire.Dashboard;
using Wms.Application.Identity;

namespace Wms.ASP.Jobs;

public sealed class WmsHangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true &&
            httpContext.User.IsInRole(WmsRoleNames.Administrator);
    }
}
