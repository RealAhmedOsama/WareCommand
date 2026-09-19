using Microsoft.Extensions.Logging;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identity;

public sealed class AuthenticationAuditService(
    WmsDbContext context,
    ILogger<AuthenticationAuditService> logger) : IAuthenticationAuditService
{
    public async Task RecordAsync(
        string eventType,
        bool succeeded,
        string? userId = null,
        string? userName = null,
        string? remoteIpAddress = null,
        string? userAgent = null,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new WmsAuthenticationEvent
        {
            EventType = Trim(eventType, 100) ?? "Unknown",
            Succeeded = succeeded,
            UserId = Trim(userId, 450),
            UserName = Trim(userName, 256),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            RemoteIpAddress = Trim(remoteIpAddress, 64),
            UserAgent = Trim(userAgent, 512),
            Details = Trim(details, 1000)
        };

        try
        {
            context.AuthenticationEvents.Add(auditEvent);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Authentication must not expose persistence failures to an attacker, but
            // loss of an audit record must remain visible to operators.
            logger.LogError(
                exception,
                "Could not persist authentication audit event {EventType} for user {UserId}",
                auditEvent.EventType,
                auditEvent.UserId);
        }
    }

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
