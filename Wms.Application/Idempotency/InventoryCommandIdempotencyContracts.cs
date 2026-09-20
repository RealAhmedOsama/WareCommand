using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wms.Application.Common;
using Wms.Application.Context;

namespace Wms.Application.Idempotency;

public sealed record InventoryCommandIdempotencyRequest(
    string OperationType,
    string CommandKey,
    string CallerScope,
    string RequestHash,
    string CorrelationId,
    string? ActorUserId,
    int? WarehouseId,
    TimeSpan? RetentionWindow = null);

public sealed record InventoryCommandIdempotencyLease(
    int RecordId,
    string OperationType,
    string CommandKey,
    string CallerScope,
    TimeSpan RetentionWindow);

public sealed record InventoryCommandIdempotencyDecision(
    bool ShouldExecute,
    InventoryCommandIdempotencyLease? Lease = null,
    string? ResultType = null,
    string? ResultPayloadJson = null,
    string? ResultReference = null)
{
    public bool IsReplay => !ShouldExecute;
}

public sealed record InventoryCommandIdempotencyCompletion(
    string ResultType,
    string ResultPayloadJson,
    string? ResultReference = null);

public interface IInventoryCommandIdempotencyService
{
    Task<Result<InventoryCommandIdempotencyDecision>> BeginAsync(
        InventoryCommandIdempotencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the tracked command record without saving it. Inventory callers
    /// must save this change in the same database transaction as their mutation.
    /// </summary>
    Task CompleteAsync(
        InventoryCommandIdempotencyLease lease,
        InventoryCommandIdempotencyCompletion completion,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a tracked command failed without saving it. Callers that need a
    /// durable failed state must save it after the business transaction is safe.
    /// </summary>
    Task MarkFailedAsync(
        InventoryCommandIdempotencyLease lease,
        string failureCode,
        string? failureMessage = null,
        CancellationToken cancellationToken = default);

    Task<int> PruneAsync(
        DateTimeOffset beforeUtc,
        CancellationToken cancellationToken = default);
}

public static class InventoryCommandRequestHasher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Compute(string operationType, object request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationType);
        ArgumentNullException.ThrowIfNull(request);

        var canonicalPayload = JsonSerializer.Serialize(
            new { OperationType = operationType.Trim(), Request = request },
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));
    }
}

public static class InventoryCommandIdempotencyScope
{
    public static string For(
        IRequestContext? requestContext,
        string userId,
        int? warehouseId)
    {
        var sourceClient = requestContext?.SourceClient ?? "Application";
        var source = Normalize(sourceClient, "Application");
        var actor = Normalize(userId, "unknown");
        var warehouse = warehouseId?.ToString(CultureInfo.InvariantCulture) ?? "global";
        return source + ":" + actor + ":warehouse:" + warehouse;
    }

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }
}

public static class InventoryCommandJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static Result<T> DeserializeResult<T>(InventoryCommandIdempotencyDecision decision)
    {
        if (!decision.IsReplay || string.IsNullOrWhiteSpace(decision.ResultPayloadJson))
        {
            return Result.Failure<T>(WmsErrors.Unexpected(
                "idempotency.result_unavailable",
                "The original command result is not available for replay."));
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(decision.ResultPayloadJson, Options);
            return value is null
                ? Result.Failure<T>(WmsErrors.Unexpected(
                    "idempotency.result_unavailable",
                    "The original command result is not available for replay."))
                : Result.Success(value);
        }
        catch (JsonException)
        {
            return Result.Failure<T>(WmsErrors.Unexpected(
                "idempotency.result_unavailable",
                "The original command result is not available for replay."));
        }
    }
}
