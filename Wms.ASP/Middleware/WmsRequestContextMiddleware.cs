using Wms.Application.Context;
using Wms.Infrastructure.Logging;

namespace Wms.ASP.Middleware;

public sealed class WmsRequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        IRequestContext requestContext,
        IWmsOperationContextAccessor operationContextAccessor,
        ILogger<WmsRequestContextMiddleware> logger)
    {
        requestContext.Initialize(
            WmsExecutionIdentifiers.NormalizeOptional(
                httpContext.Request.Headers[WmsOperationContextPropagation.CorrelationIdHeader].ToString()),
            "Web",
            httpContext.Connection.RemoteIpAddress?.ToString(),
            httpContext.Request.Headers.UserAgent.ToString(),
            httpContext.Request.Headers[WmsOperationContextPropagation.IdempotencyKeyHeader].ToString());

        var operationContext = new WmsOperationContext(
            requestContext.CorrelationId,
            WmsExecutionIdentifiers.Normalize(
                httpContext.Request.Headers[WmsOperationContextPropagation.OperationIdHeader].ToString()),
            requestContext.SourceClient,
            "http.request",
            WmsExecutionIdentifiers.NormalizeOptional(
                httpContext.Request.Headers[WmsOperationContextPropagation.ReferenceIdHeader].ToString()),
            RequestId: httpContext.TraceIdentifier);
        using var loggingScope = WmsLogging.BeginOperation(
            logger,
            operationContextAccessor,
            operationContext);

        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[WmsOperationContextPropagation.CorrelationIdHeader] =
                requestContext.CorrelationId;
            httpContext.Response.Headers[WmsOperationContextPropagation.OperationIdHeader] =
                operationContext.OperationId;
            if (!string.IsNullOrWhiteSpace(operationContext.ReferenceId))
            {
                httpContext.Response.Headers[WmsOperationContextPropagation.ReferenceIdHeader] =
                    operationContext.ReferenceId;
            }
            return Task.CompletedTask;
        });

        await next(httpContext);
    }
}
