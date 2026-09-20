using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Wms.ASP.Health;

public static class WmsHealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        var content = report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy";
        return context.Response.WriteAsync(content);
    }
}
