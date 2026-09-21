using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Integrations;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Integrations;

public sealed class IntegrationInboxService(
    WmsDbContext context) : IIntegrationInboxService
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);
    private const int MaximumErrorLength = 2_000;

    public async Task<IntegrationInboxClaim> TryBeginAsync(
        IntegrationInboxRequest request,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var payloadHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.PayloadJson)))
            .ToLowerInvariant();
        var message = await context.IntegrationInbox.SingleOrDefaultAsync(
            item => item.SourceSystem == request.SourceSystem &&
                item.ExternalMessageId == request.ExternalMessageId,
            cancellationToken);
        if (message is null)
        {
            message = new WmsIntegrationInboxEntity
            {
                SourceSystem = request.SourceSystem.Trim(),
                ExternalMessageId = request.ExternalMessageId.Trim(),
                EventType = request.EventType.Trim(),
                PayloadHash = payloadHash,
                PayloadJson = request.PayloadJson,
                CorrelationId = request.CorrelationId,
                Status = WmsIntegrationInboxStatuses.Processing,
                AttemptCount = 1,
                ReceivedAtUtc = nowUtc,
                LeaseUntilUtc = nowUtc.Add(ProcessingLease)
            };
            context.IntegrationInbox.Add(message);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return new IntegrationInboxClaim(message.Id, ShouldProcess: true, WasDuplicate: false);
            }
            catch (DbUpdateException)
            {
                context.Entry(message).State = EntityState.Detached;
                message = await context.IntegrationInbox.SingleAsync(
                    item => item.SourceSystem == request.SourceSystem &&
                        item.ExternalMessageId == request.ExternalMessageId,
                    cancellationToken);
            }
        }

        if (!string.Equals(message.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Inbox message '{request.SourceSystem}/{request.ExternalMessageId}' was reused with a different payload.");
        }

        if (message.Status is WmsIntegrationInboxStatuses.Succeeded or WmsIntegrationInboxStatuses.DeadLettered)
        {
            return new IntegrationInboxClaim(
                message.Id,
                ShouldProcess: false,
                WasDuplicate: true,
                "The external message was already completed.");
        }

        if (message.Status == WmsIntegrationInboxStatuses.Processing &&
            message.LeaseUntilUtc.HasValue &&
            message.LeaseUntilUtc > nowUtc)
        {
            return new IntegrationInboxClaim(
                message.Id,
                ShouldProcess: false,
                WasDuplicate: true,
                "The external message is already being processed.");
        }

        message.Status = WmsIntegrationInboxStatuses.Processing;
        message.AttemptCount++;
        message.LeaseUntilUtc = nowUtc.Add(ProcessingLease);
        message.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
        return new IntegrationInboxClaim(message.Id, ShouldProcess: true, WasDuplicate: false);
    }

    public async Task CompleteAsync(
        long messageId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var message = await GetAsync(messageId, cancellationToken);
        message.Status = WmsIntegrationInboxStatuses.Succeeded;
        message.ProcessedAtUtc = completedAtUtc;
        message.LeaseUntilUtc = null;
        message.LastError = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        long messageId,
        Exception exception,
        DateTimeOffset failedAtUtc,
        bool deadLetter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = await GetAsync(messageId, cancellationToken);
        message.Status = deadLetter
            ? WmsIntegrationInboxStatuses.DeadLettered
            : WmsIntegrationInboxStatuses.Failed;
        message.ProcessedAtUtc = deadLetter ? failedAtUtc : null;
        message.LeaseUntilUtc = null;
        message.LastError = exception.Message.Length <= MaximumErrorLength
            ? exception.Message
            : exception.Message[..MaximumErrorLength];
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<WmsIntegrationInboxEntity> GetAsync(
        long messageId,
        CancellationToken cancellationToken) =>
        await context.IntegrationInbox.SingleAsync(message => message.Id == messageId, cancellationToken);

    private static void Validate(IntegrationInboxRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExternalMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EventType);
        ArgumentNullException.ThrowIfNull(request.PayloadJson);
    }
}
