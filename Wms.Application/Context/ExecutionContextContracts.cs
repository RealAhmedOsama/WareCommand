namespace Wms.Application.Context;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IRequestContext
{
    string CorrelationId { get; }

    string SourceClient { get; }

    string? RemoteIpAddress { get; }

    string? UserAgent { get; }

    void Initialize(
        string? correlationId,
        string sourceClient,
        string? remoteIpAddress = null,
        string? userAgent = null);
}

public interface IWarehouseContext
{
    int? WarehouseId { get; }

    void SetWarehouse(int? warehouseId);
}
