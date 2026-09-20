using Wms.Application.Context;

namespace Wms.Infrastructure.Auditing;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class WmsRequestContext(string defaultSourceClient = "Application") : IRequestContext
{
    public string CorrelationId { get; private set; } = WmsExecutionIdentifiers.NewCorrelationId();

    public string SourceClient { get; private set; } = NormalizeSource(defaultSourceClient);

    public string? IdempotencyKey { get; private set; }

    public string? RemoteIpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public void Initialize(
        string? correlationId,
        string sourceClient,
        string? remoteIpAddress = null,
        string? userAgent = null,
        string? idempotencyKey = null)
    {
        CorrelationId = WmsExecutionIdentifiers.Normalize(correlationId);
        SourceClient = NormalizeSource(sourceClient);
        IdempotencyKey = WmsExecutionIdentifiers.NormalizeOptional(idempotencyKey, 250);
        RemoteIpAddress = Trim(remoteIpAddress, 64);
        UserAgent = Trim(userAgent, 512);
    }

    private static string NormalizeSource(string? value) =>
        Trim(value, 50) ?? "Application";

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Length <= maximumLength
            ? value.Trim()
            : value.Trim()[..maximumLength];
    }
}

public sealed class WmsWarehouseContext : IWarehouseContext
{
    public int? WarehouseId { get; private set; }

    public void SetWarehouse(int? warehouseId)
    {
        WarehouseId = warehouseId;
    }
}
