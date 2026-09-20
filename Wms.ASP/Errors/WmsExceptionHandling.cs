using System.Data.Common;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Common;
using Wms.Application.Context;

namespace Wms.ASP.Errors;

public static class WmsErrorHandling
{
    public const string ErrorReferenceItem = "Wms.ErrorReference";

    public static string? NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return normalized.Length is 0 or > 100 ? null : normalized;
    }

    public static bool WantsProblemDetails(HttpContext context)
    {
        var accept = context.Request.Headers.Accept.ToString();
        return context.Request.Path.StartsWithSegments("/api") ||
               accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) ||
               accept.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetErrorReference(HttpContext context, IRequestContext requestContext) =>
        NormalizeCorrelationId(context.Request.Headers["X-Correlation-ID"].ToString()) ??
        (requestContext.CorrelationId.Length > 0
            ? requestContext.CorrelationId
            : context.TraceIdentifier);
}

public sealed record WmsExceptionMapping(
    int StatusCode,
    string Code,
    string Title,
    string Detail,
    bool IsRetryable = false);

public static class WmsExceptionMapper
{
    public static WmsExceptionMapping Map(Exception exception) => exception switch
    {
        ArgumentException => new(
            StatusCodes.Status400BadRequest,
            "request.invalid",
            "Invalid request",
            "The request could not be processed."),
        KeyNotFoundException => new(
            StatusCodes.Status404NotFound,
            "resource.not_found",
            "Resource not found",
            "The requested resource could not be found."),
        UnauthorizedAccessException => new(
            StatusCodes.Status403Forbidden,
            "authorization.forbidden",
            "Access denied",
            "You do not have permission to perform this operation."),
        DbUpdateConcurrencyException => new(
            StatusCodes.Status409Conflict,
            "data.concurrency_conflict",
            "Concurrency conflict",
            "The record changed while you were working. Reload it and try again.",
            IsRetryable: true),
        DbUpdateException dbUpdateException when IsUniqueConstraintViolation(dbUpdateException) => new(
            StatusCodes.Status409Conflict,
            "data.conflict",
            "Data conflict",
            "The requested change conflicts with existing data."),
        DbUpdateException => new(
            StatusCodes.Status503ServiceUnavailable,
            "data.dependency_failure",
            "Data service unavailable",
            "The data service could not complete the request. Please try again.",
            IsRetryable: true),
        _ => new(
            StatusCodes.Status500InternalServerError,
            "server.unexpected_error",
            "Unexpected error",
            "The request could not be completed. Use the error reference when contacting support.")
    };

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is DbException dbException &&
                dbException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            if (string.Equals(sqlState, "23505", StringComparison.Ordinal))
            {
                return true;
            }

            if (current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class WmsExceptionHandler(
    ILogger<WmsExceptionHandler> logger,
    IRequestContext requestContext) : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException &&
            (cancellationToken.IsCancellationRequested || httpContext.RequestAborted.IsCancellationRequested))
        {
            return false;
        }

        var mapping = WmsExceptionMapper.Map(exception);
        var errorReference = WmsErrorHandling.GetErrorReference(httpContext, requestContext);
        httpContext.Items[WmsErrorHandling.ErrorReferenceItem] = errorReference;

        logger.LogError(
            exception,
            "Unhandled request exception {ErrorCode}; error reference {ErrorReference}; path {RequestPath}",
            mapping.Code,
            errorReference,
            httpContext.Request.Path);

        if (!WmsErrorHandling.WantsProblemDetails(httpContext))
        {
            return false;
        }

        httpContext.Response.StatusCode = mapping.StatusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";

        var problemDetails = new ProblemDetails
        {
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = mapping.Detail,
            Type = $"https://warecommand.local/problems/{mapping.Code}",
            Instance = httpContext.Request.Path
        };
        problemDetails.Extensions["errorCode"] = mapping.Code;
        problemDetails.Extensions["errorReference"] = errorReference;
        problemDetails.Extensions["retryable"] = mapping.IsRetryable;

        await JsonSerializer.SerializeAsync(
            httpContext.Response.Body,
            problemDetails,
            JsonOptions,
            cancellationToken: cancellationToken);
        return true;
    }
}
