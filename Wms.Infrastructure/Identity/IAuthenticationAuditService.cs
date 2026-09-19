namespace Wms.Infrastructure.Identity;

public interface IAuthenticationAuditService
{
    Task RecordAsync(
        string eventType,
        bool succeeded,
        string? userId = null,
        string? userName = null,
        string? remoteIpAddress = null,
        string? userAgent = null,
        string? details = null,
        CancellationToken cancellationToken = default);
}
