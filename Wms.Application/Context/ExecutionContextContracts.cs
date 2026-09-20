namespace Wms.Application.Context;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IRequestContext
{
    string CorrelationId { get; }

    string SourceClient { get; }

    string? IdempotencyKey { get; }

    string? RemoteIpAddress { get; }

    string? UserAgent { get; }

    void Initialize(
        string? correlationId,
        string sourceClient,
        string? remoteIpAddress = null,
        string? userAgent = null,
        string? idempotencyKey = null);
}

public interface IWarehouseContext
{
    int? WarehouseId { get; }

    void SetWarehouse(int? warehouseId);
}

public static class WmsExecutionIdentifiers
{
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    public static string NewOperationId() => Guid.NewGuid().ToString("N");

    public static string Normalize(string? value, int maximumLength = 100)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NewCorrelationId();
        }

        var normalized = new string(value
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return normalized.Length == 0 || normalized.Length > maximumLength
            ? NewCorrelationId()
            : normalized;
    }

    public static string? NormalizeOptional(string? value, int maximumLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '.')
            .ToArray());
        return normalized.Length == 0 || normalized.Length > maximumLength ? null : normalized;
    }
}

/// <summary>
/// The operation envelope carried across host, application, job, API, and integration boundaries.
/// It deliberately contains identifiers and safe context only; payloads and secrets do not belong here.
/// </summary>
public sealed record WmsOperationContext(
    string CorrelationId,
    string OperationId,
    string SourceClient,
    string OperationName,
    string? ReferenceId = null,
    string? UserId = null,
    int? WarehouseId = null,
    string? RequestId = null);

/// <summary>
/// Provides the current asynchronous operation context and supports nested operations.
/// </summary>
public interface IWmsOperationContextAccessor
{
    WmsOperationContext? Current { get; }

    IDisposable Begin(WmsOperationContext context);
}

/// <summary>
/// Stable header names used when an operation crosses an HTTP or integration boundary.
/// </summary>
public static class WmsOperationContextPropagation
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string OperationIdHeader = "X-Operation-ID";
    public const string ReferenceIdHeader = "X-Reference-ID";
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IReadOnlyDictionary<string, string> ToHeaders(WmsOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CorrelationIdHeader] = context.CorrelationId,
            [OperationIdHeader] = context.OperationId
        };

        if (!string.IsNullOrWhiteSpace(context.ReferenceId))
        {
            headers[ReferenceIdHeader] = context.ReferenceId;
        }

        return headers;
    }
}
