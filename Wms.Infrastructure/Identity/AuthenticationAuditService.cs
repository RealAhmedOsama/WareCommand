using Microsoft.Extensions.Logging;
using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Identity;

public sealed class AuthenticationAuditService(
    WmsDbContext context,
    IAuditWriter auditWriter,
    IClock clock,
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
            OccurredAtUtc = clock.UtcNow,
            RemoteIpAddress = Trim(remoteIpAddress, 64),
            UserAgent = Trim(userAgent, 512),
            Details = Trim(details, 1000)
        };

        try
        {
            context.AuthenticationEvents.Add(auditEvent);
            var (action, entityType, entityId, before, after) = BuildAuditProjection(
                auditEvent.EventType,
                auditEvent.UserId);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    action,
                    entityType,
                    entityId,
                    Succeeded: succeeded,
                    Before: before,
                    After: after,
                    Details: auditEvent.Details),
                cancellationToken);
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

    private static (
        string Action,
        string EntityType,
        string? EntityId,
        IReadOnlyDictionary<string, object?>? Before,
        IReadOnlyDictionary<string, object?>? After) BuildAuditProjection(
        string eventType,
        string? userId)
    {
        if (eventType == WmsAuthenticationEventTypes.AccountCreated)
        {
            return (
                WmsAuditActions.AccountCreated,
                WmsAuditEntityTypes.User,
                userId,
                null,
                userId is null
                    ? null
                    : new Dictionary<string, object?>
                    {
                        ["subjectUserId"] = userId,
                        ["isActive"] = true
                    });
        }

        if (eventType is WmsAuthenticationEventTypes.AccountDisabledByAdmin
            or WmsAuthenticationEventTypes.AccountEnabledByAdmin)
        {
            var isActive = eventType == WmsAuthenticationEventTypes.AccountEnabledByAdmin;
            return (
                WmsAuditActions.AccountStatusChanged,
                WmsAuditEntityTypes.User,
                userId,
                new Dictionary<string, object?>
                {
                    ["isActive"] = !isActive
                },
                new Dictionary<string, object?>
                {
                    ["isActive"] = isActive
                });
        }

        return (
            WmsAuditActions.AuthenticationEvent,
            WmsAuditEntityTypes.Authentication,
            eventType,
            null,
            userId is null
                ? null
                : new Dictionary<string, object?>
                {
                    ["subjectUserId"] = userId
                });
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
