using Wms.Application.Context;
using Wms.ASP.Errors;

namespace Wms.ASP.Middleware;

public sealed class WmsRequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        IRequestContext requestContext,
        ILogger<WmsRequestContextMiddleware> logger)
    {
        requestContext.Initialize(
            WmsErrorHandling.NormalizeCorrelationId(
                httpContext.Request.Headers["X-Correlation-ID"].ToString()),
            "Web",
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString());

        using var loggingScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = requestContext.CorrelationId,
            ["SourceClient"] = requestContext.SourceClient
        });

        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers["X-Correlation-ID"] = requestContext.CorrelationId;
            return Task.CompletedTask;
        });

        await next(httpContext);
    }
}
