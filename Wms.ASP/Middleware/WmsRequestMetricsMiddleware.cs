using System.Diagnostics;
using Wms.Application.Telemetry;

namespace Wms.ASP.Middleware;

public sealed class WmsRequestMetricsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            WmsTelemetry.RecordRequest(
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }
}
