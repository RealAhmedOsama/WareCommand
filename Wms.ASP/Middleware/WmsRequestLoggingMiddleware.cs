using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Application.Logging;
using Wms.Infrastructure.Logging;

namespace Wms.ASP.Middleware;

public sealed class WmsRequestLoggingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext httpContext,
        IRequestContext requestContext,
        ICurrentUser currentUser,
        IWarehouseContext warehouseContext,
        IWmsOperationContextAccessor operationContextAccessor,
        WmsLoggingOptions loggingOptions,
        IHostEnvironment environment,
        ILogger<WmsRequestLoggingMiddleware> logger)
    {
        var operationContext = operationContextAccessor.Current ?? new WmsOperationContext(
            requestContext.CorrelationId,
            WmsExecutionIdentifiers.NewOperationId(),
            requestContext.SourceClient,
            "http.request",
            RequestId: httpContext.TraceIdentifier);
        operationContext = operationContext with
        {
            UserId = currentUser.UserId,
            WarehouseId = warehouseContext.WarehouseId
        };

        using var loggingScope = WmsLogging.BeginOperation(
            logger,
            operationContextAccessor,
            operationContext);
        var stopwatch = Stopwatch.StartNew();
        var failed = false;

        logger.LogInformation(
            WmsLogEvents.RequestStarted,
            "HTTP request started {HttpMethod} {RequestPath} in {EnvironmentName}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value ?? "/",
            environment.EnvironmentName);

        try
        {
            await next(httpContext);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var durationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            var statusCode = failed && httpContext.Response.StatusCode < 400
                ? StatusCodes.Status500InternalServerError
                : httpContext.Response.StatusCode;
            var outcome = failed ? "exception" : statusCode >= 400 ? "failure" : "success";

            if (failed)
            {
                logger.LogWarning(
                    WmsLogEvents.RequestFailed,
                    "HTTP request completed with an exception {StatusCode} {DurationMs}ms {Outcome}",
                    statusCode,
                    durationMilliseconds,
                    outcome);
            }
            else
            {
                logger.LogInformation(
                    WmsLogEvents.RequestCompleted,
                    "HTTP request completed with {StatusCode} {DurationMs}ms {Outcome}",
                    statusCode,
                    durationMilliseconds,
                    outcome);
            }

            if (durationMilliseconds >= loggingOptions.SlowOperationThresholdMilliseconds)
            {
                logger.LogWarning(
                    WmsLogEvents.SlowOperation,
                    "Slow HTTP request detected {RequestPath} {DurationMs}ms",
                    httpContext.Request.Path.Value ?? "/",
                    durationMilliseconds);
            }
        }
    }
}
