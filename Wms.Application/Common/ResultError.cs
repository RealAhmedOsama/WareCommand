using Wms.Domain.Services;

namespace Wms.Application.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Concurrency,
    BusinessRule,
    Dependency,
    Unexpected,
    Cancelled
}

public sealed record ResultError(
    string Code,
    ErrorType Type,
    string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    bool IsRetryable = false)
{
    public bool IsExpected => Type is not ErrorType.Unexpected;
}

public static class WmsErrors
{
    public static ResultError FromException(
        Exception exception,
        string code,
        string safeMessage)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is LocationConstraintViolationException locationConstraint)
            {
                return Conflict(locationConstraint.Code, locationConstraint.Message);
            }

            if (current is ConcurrencyConflictException concurrencyConflict)
            {
                return Concurrency(concurrencyConflict.Code, concurrencyConflict.Message);
            }

            var exceptionType = current.GetType();
            if (exceptionType.Name == "DbUpdateConcurrencyException")
            {
                return Concurrency(
                    "data.concurrency_conflict",
                    "The record changed while you were working. Reload it and try again.");
            }

            if (exceptionType.Name == "DbUpdateException")
            {
                var sqlState = exceptionType.GetProperty("SqlState")?.GetValue(current) as string;
                if (sqlState == "23505" ||
                    current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(
                        "data.conflict",
                        "The requested change conflicts with existing data.");
                }

                return Dependency(
                    "data.dependency_failure",
                    "The data service could not complete the request. Please try again.");
            }
        }

        return Unexpected(code, safeMessage);
    }

    public static ResultError Validation(
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(code, ErrorType.Validation, message, fieldErrors);

    public static ResultError NotFound(string code, string message) =>
        new(code, ErrorType.NotFound, message);

    public static ResultError Conflict(string code, string message) =>
        new(code, ErrorType.Conflict, message);

    public static ResultError Unauthorized(string code, string message) =>
        new(code, ErrorType.Unauthorized, message);

    public static ResultError Forbidden(string code, string message) =>
        new(code, ErrorType.Forbidden, message);

    public static ResultError Concurrency(string code, string message) =>
        new(code, ErrorType.Concurrency, message, IsRetryable: true);

    public static ResultError BusinessRule(string code, string message) =>
        new(code, ErrorType.BusinessRule, message);

    public static ResultError Dependency(string code, string message, bool isRetryable = true) =>
        new(code, ErrorType.Dependency, message, IsRetryable: isRetryable);

    public static ResultError Unexpected(string code, string message) =>
        new(code, ErrorType.Unexpected, message);

    public static ResultError Cancelled(string code = "request.cancelled") =>
        new(code, ErrorType.Cancelled, "The request was cancelled.");
}
