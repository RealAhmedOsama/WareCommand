using Wms.Application.Context;

namespace Wms.ASP.Middleware;

public sealed class WmsRequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        IRequestContext requestContext)
    {
        requestContext.Initialize(
            httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault(),
            "Web",
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());

        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers["X-Correlation-ID"] = requestContext.CorrelationId;
            return Task.CompletedTask;
        });

        await next(httpContext);
    }
}
