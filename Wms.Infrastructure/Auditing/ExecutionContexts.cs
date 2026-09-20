using Wms.Application.Context;

namespace Wms.Infrastructure.Auditing;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class WmsRequestContext(string defaultSourceClient = "Application") : IRequestContext
{
    public string CorrelationId { get; private set; } = NewCorrelationId();

    public string SourceClient { get; private set; } = NormalizeSource(defaultSourceClient);

    public string? RemoteIpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public void Initialize(
        string? correlationId,
        string sourceClient,
        string? remoteIpAddress = null,
        string? userAgent = null)
    {
        CorrelationId = NormalizeCorrelationId(correlationId);
        SourceClient = NormalizeSource(sourceClient);
        RemoteIpAddress = Trim(remoteIpAddress, 64);
        UserAgent = Trim(userAgent, 512);
    }

    private static string NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return NewCorrelationId();
        }

        var normalized = new string(value
            .Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return normalized.Length is 0 or > 100 ? NewCorrelationId() : normalized;
    }

    private static string NormalizeSource(string? value) =>
        Trim(value, 50) ?? "Application";

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N");

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
