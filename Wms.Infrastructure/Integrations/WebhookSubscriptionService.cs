using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Context;
using Wms.Application.Integrations;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Integrations;

public sealed class DataProtectionWebhookSecretProtector(
    IDataProtectionProvider provider) : IWebhookSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("WareCommand.Webhooks.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

public sealed class WebhookSubscriptionService(
    WmsDbContext context,
    IWebhookSecretProtector secretProtector,
    IClock clock) : IWebhookSubscriptionService
{
    public async Task<IReadOnlyList<WebhookSubscriptionDto>> ListAsync(
        CancellationToken cancellationToken = default) =>
        (await context.WebhookSubscriptions
            .AsNoTracking()
            .OrderBy(subscription => subscription.Id)
            .ToListAsync(cancellationToken))
        .Select(ToDto)
        .ToList();

    public async Task<WebhookSubscriptionIssue> CreateAsync(
        WebhookSubscriptionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(request);
        var now = clock.UtcNow;
        var secret = CreateSecret();
        var entity = new WmsWebhookSubscriptionEntity
        {
            Name = normalized.Name,
            EndpointUrl = normalized.EndpointUrl,
            EventTypesJson = JsonSerializer.Serialize(normalized.EventTypes),
            WarehouseIdsJson = JsonSerializer.Serialize(normalized.WarehouseIds),
            SecretCiphertext = secretProtector.Protect(secret),
            SecretVersion = 1,
            Status = WmsWebhookSubscriptionStatuses.Active,
            MaximumAttempts = normalized.MaximumAttempts,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        context.WebhookSubscriptions.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return new WebhookSubscriptionIssue(ToDto(entity), secret);
    }

    public async Task<WebhookSubscriptionRotation> RotateSecretAsync(
        long subscriptionId,
        TimeSpan overlap,
        CancellationToken cancellationToken = default)
    {
        if (overlap < TimeSpan.Zero || overlap > TimeSpan.FromDays(30))
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Secret overlap must be between 0 and 30 days.");
        }

        var entity = await context.WebhookSubscriptions.SingleAsync(
            subscription => subscription.Id == subscriptionId,
            cancellationToken);
        var now = clock.UtcNow;
        var secret = CreateSecret();
        entity.PreviousSecretCiphertext = entity.SecretCiphertext;
        entity.PreviousSecretVersion = entity.SecretVersion;
        entity.PreviousSecretValidUntilUtc = now.Add(overlap);
        entity.SecretCiphertext = secretProtector.Protect(secret);
        entity.SecretVersion++;
        entity.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
        return new WebhookSubscriptionRotation(
            secret,
            entity.PreviousSecretValidUntilUtc.Value,
            ToDto(entity));
    }

    public async Task<bool> SetStatusAsync(
        long subscriptionId,
        string status,
        CancellationToken cancellationToken = default)
    {
        if (status is not (
            WmsWebhookSubscriptionStatuses.Active or
            WmsWebhookSubscriptionStatuses.Disabled or
            WmsWebhookSubscriptionStatuses.Revoked))
        {
            throw new ArgumentException($"Unsupported webhook status '{status}'.", nameof(status));
        }

        var entity = await context.WebhookSubscriptions.SingleOrDefaultAsync(
            subscription => subscription.Id == subscriptionId,
            cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.Status = status;
        entity.DisabledAtUtc = status == WmsWebhookSubscriptionStatuses.Active
            ? null
            : clock.UtcNow;
        entity.UpdatedAtUtc = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal string UnprotectSecret(WmsWebhookSubscriptionEntity entity) =>
        secretProtector.Unprotect(entity.SecretCiphertext);

    internal static IReadOnlyList<string> ReadEventTypes(WmsWebhookSubscriptionEntity entity) =>
        JsonSerializer.Deserialize<List<string>>(entity.EventTypesJson) ?? [];

    internal static IReadOnlyList<int> ReadWarehouseIds(WmsWebhookSubscriptionEntity entity) =>
        JsonSerializer.Deserialize<List<int>>(entity.WarehouseIdsJson) ?? [];

    private static WebhookSubscriptionCreateRequest Normalize(WebhookSubscriptionCreateRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        if (!Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ArgumentException("Webhook endpoint must be an absolute HTTP or HTTPS URL.", nameof(request));
        }

        if (request.MaximumAttempts is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var eventTypes = (request.EventTypes ?? [])
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (eventTypes.Any(type => !WmsIntegrationEventTypes.IsSupported(type)))
        {
            throw new ArgumentException("The subscription contains an unsupported event type.", nameof(request));
        }

        var warehouseIds = (request.WarehouseIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (warehouseIds.Length != (request.WarehouseIds ?? []).Count(id => id > 0))
        {
            throw new ArgumentException("Warehouse scope contains duplicate IDs.", nameof(request));
        }

        return new WebhookSubscriptionCreateRequest(
            request.Name.Trim(),
            endpoint.ToString(),
            eventTypes,
            warehouseIds,
            request.MaximumAttempts);
    }

    private static string CreateSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"whsec_{Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=')}";
    }

    private static WebhookSubscriptionDto ToDto(WmsWebhookSubscriptionEntity entity) =>
        new(
            entity.Id,
            entity.Name,
            entity.EndpointUrl,
            ReadEventTypes(entity).ToArray(),
            ReadWarehouseIds(entity).ToArray(),
            entity.Status,
            entity.SecretVersion,
            entity.MaximumAttempts,
            entity.CreatedAtUtc,
            entity.LastDeliveryAtUtc,
            entity.DisabledAtUtc);
}

public sealed class UnconfiguredWebhookDeliveryTransport : IWebhookDeliveryTransport
{
    public Task<WebhookDeliveryResult> SendAsync(
        WebhookDeliveryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WebhookDeliveryResult(
            Succeeded: false,
            Retryable: false,
            Error: "No webhook delivery provider is configured; external network delivery is disabled."));
}
