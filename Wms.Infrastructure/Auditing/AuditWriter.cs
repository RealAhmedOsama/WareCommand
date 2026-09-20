using Wms.Application.Auditing;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Auditing;

public sealed class AuditWriter(
    WmsDbContext context,
    ICurrentUser currentUser,
    IClock clock,
    IRequestContext requestContext,
    IWarehouseContext warehouseContext) : IAuditWriter
{
    public Task RecordAsync(
        AuditRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(record.Action))
        {
            throw new ArgumentException("An audit action is required.", nameof(record));
        }

        if (string.IsNullOrWhiteSpace(record.EntityType))
        {
            throw new ArgumentException("An audit entity type is required.", nameof(record));
        }

        var actorUserId = Trim(record.ActorUserId ??
                                (currentUser.IsAuthenticated ? currentUser.UserId : null), 450);
        var actorUserName = Trim(record.ActorUserName ??
                                  (currentUser.IsAuthenticated ? currentUser.UserName : null), 256);
        var correlationId = Trim(requestContext.CorrelationId, 100) ?? Guid.NewGuid().ToString("N");
        var sourceClient = Trim(requestContext.SourceClient, 50) ?? "Application";

        context.AuditEntries.Add(AuditEntry.Create(
            clock.UtcNow,
            actorUserId,
            actorUserName,
            Trim(record.Action, 100)!,
            Trim(record.EntityType, 100)!,
            Trim(record.EntityId, 200),
            record.WarehouseId ?? warehouseContext.WarehouseId,
            correlationId,
            sourceClient,
            Trim(requestContext.RemoteIpAddress, 64),
            Trim(requestContext.UserAgent, 512),
            record.Succeeded,
            AuditMetadataRedactor.TrimText(record.Details, 1_000),
            AuditMetadataRedactor.Serialize(record.Before),
            AuditMetadataRedactor.Serialize(record.After)));

        return Task.CompletedTask;
    }

    private static string? Trim(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
