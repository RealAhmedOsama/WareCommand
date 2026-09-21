using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Application.ApiClients;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Context;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;
using Wms.Infrastructure.Identity;

namespace Wms.Infrastructure.ApiClients;

public sealed class ApiClientCredentialService(
    WmsDbContext context,
    UserManager<WmsUser> userManager,
    IAuditWriter auditWriter,
    IClock clock,
    ILogger<ApiClientCredentialService> logger) : IApiClientCredentialService
{
    private const string ActiveStatus = "Active";
    private const string RevokedStatus = "Revoked";
    private static readonly TimeSpan DefaultRotationOverlap = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<ApiClientIssue>> CreateAsync(
        ApiClientCreateRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await AuthorizeActorAsync(actorUserId, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.ToFailure<ApiClientIssue>();
        }

        var normalized = await NormalizeAsync(request, cancellationToken);
        if (normalized.IsFailure)
        {
            return normalized.ToFailure<ApiClientIssue>();
        }

        var now = clock.UtcNow;
        var clientId = NewClientId();
        var secret = NewSecret();
        var entity = new WmsApiClientEntity
        {
            ClientId = clientId,
            Name = normalized.Value.Name,
            Owner = normalized.Value.Owner,
            Status = ActiveStatus,
            ScopesJson = Serialize(normalized.Value.Scopes),
            WarehouseIdsJson = Serialize(normalized.Value.WarehouseIds),
            HasGlobalWarehouseAccess = normalized.Value.HasGlobalWarehouseAccess,
            IpRestrictionsJson = Serialize(normalized.Value.IpRestrictions),
            SecretHash = ApiClientSecretHasher.Hash(secret),
            SecretVersion = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            ExpiresAtUtc = normalized.Value.ExpiresAtUtc
        };

        try
        {
            context.ApiClients.Add(entity);
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ApiClientCreated,
                    WmsAuditEntityTypes.ApiClient,
                    entity.ClientId,
                    After: new Dictionary<string, object?>
                    {
                        ["name"] = entity.Name,
                        ["owner"] = entity.Owner,
                        ["scopeCount"] = normalized.Value.Scopes.Count,
                        ["warehouseCount"] = normalized.Value.WarehouseIds.Count,
                        ["globalWarehouseAccess"] = entity.HasGlobalWarehouseAccess,
                        ["secretVersion"] = entity.SecretVersion,
                        ["expiresAtUtc"] = entity.ExpiresAtUtc
                    },
                    ActorUserId: actor.Value.Id,
                    ActorUserName: actor.Value.UserName),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(new ApiClientIssue(ToDto(entity), secret));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create API client {ApiClientName}", entity.Name);
            return Result.Failure<ApiClientIssue>(WmsErrors.FromException(
                exception,
                "api_client.create_failed",
                "The API client could not be created."));
        }
    }

    public async Task<Result<IReadOnlyList<ApiClientDto>>> ListAsync(
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await AuthorizeActorAsync(actorUserId, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.ToFailure<IReadOnlyList<ApiClientDto>>();
        }

        var clients = await context.ApiClients
            .AsNoTracking()
            .OrderBy(client => client.Name)
            .Select(client => ToDto(client))
            .ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ApiClientDto>>(clients);
    }

    public async Task<Result<ApiClientIssue>> RotateAsync(
        string clientId,
        ApiClientRotateRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await AuthorizeActorAsync(actorUserId, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.ToFailure<ApiClientIssue>();
        }

        var entity = await context.ApiClients
            .SingleOrDefaultAsync(client => client.ClientId == clientId.Trim(), cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ApiClientIssue>(WmsErrors.NotFound(
                "api_client.not_found",
                "The API client was not found."));
        }

        if (!string.Equals(entity.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return Result.Failure<ApiClientIssue>(WmsErrors.Conflict(
                "api_client.not_active",
                "Only an active API client can be rotated."));
        }

        var overlap = request.Overlap == default ? DefaultRotationOverlap : request.Overlap;
        if (overlap < TimeSpan.Zero || overlap > TimeSpan.FromDays(30))
        {
            return Result.Failure<ApiClientIssue>(WmsErrors.Validation(
                "api_client.overlap_invalid",
                "Credential rotation overlap must be between zero and thirty days."));
        }

        var now = clock.UtcNow;
        var expiresAtUtc = request.ExpiresAtUtc ?? entity.ExpiresAtUtc;
        if (expiresAtUtc.HasValue && expiresAtUtc <= now)
        {
            return Result.Failure<ApiClientIssue>(WmsErrors.Validation(
                "api_client.expiry_invalid",
                "Credential expiry must be in the future."));
        }

        var secret = NewSecret();
        entity.PreviousSecretHash = entity.SecretHash;
        entity.PreviousSecretValidUntilUtc = now.Add(overlap);
        entity.SecretHash = ApiClientSecretHasher.Hash(secret);
        entity.SecretVersion++;
        entity.RotatedAtUtc = now;
        entity.UpdatedAtUtc = now;
        entity.ExpiresAtUtc = expiresAtUtc;

        try
        {
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ApiClientRotated,
                    WmsAuditEntityTypes.ApiClient,
                    entity.ClientId,
                    After: new Dictionary<string, object?>
                    {
                        ["secretVersion"] = entity.SecretVersion,
                        ["previousSecretValidUntilUtc"] = entity.PreviousSecretValidUntilUtc,
                        ["expiresAtUtc"] = entity.ExpiresAtUtc
                    },
                    ActorUserId: actor.Value.Id,
                    ActorUserName: actor.Value.UserName),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(new ApiClientIssue(ToDto(entity), secret));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not rotate API client {ApiClientId}", entity.ClientId);
            return Result.Failure<ApiClientIssue>(WmsErrors.FromException(
                exception,
                "api_client.rotate_failed",
                "The API client credential could not be rotated."));
        }
    }

    public async Task<Result<ApiClientDto>> RevokeAsync(
        string clientId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var actor = await AuthorizeActorAsync(actorUserId, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.ToFailure<ApiClientDto>();
        }

        var entity = await context.ApiClients
            .SingleOrDefaultAsync(client => client.ClientId == clientId.Trim(), cancellationToken);
        if (entity is null)
        {
            return Result.Failure<ApiClientDto>(WmsErrors.NotFound(
                "api_client.not_found",
                "The API client was not found."));
        }

        if (!string.Equals(entity.Status, RevokedStatus, StringComparison.Ordinal))
        {
            entity.Status = RevokedStatus;
            entity.RevokedAtUtc = clock.UtcNow;
            entity.UpdatedAtUtc = entity.RevokedAtUtc.Value;
            entity.SecretHash = string.Empty;
            entity.PreviousSecretHash = null;
            entity.PreviousSecretValidUntilUtc = null;
        }

        try
        {
            await auditWriter.RecordAsync(
                new AuditRecord(
                    WmsAuditActions.ApiClientRevoked,
                    WmsAuditEntityTypes.ApiClient,
                    entity.ClientId,
                    After: new Dictionary<string, object?>
                    {
                        ["status"] = entity.Status,
                        ["revokedAtUtc"] = entity.RevokedAtUtc
                    },
                    ActorUserId: actor.Value.Id,
                    ActorUserName: actor.Value.UserName),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success(ToDto(entity));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not revoke API client {ApiClientId}", entity.ClientId);
            return Result.Failure<ApiClientDto>(WmsErrors.FromException(
                exception,
                "api_client.revoke_failed",
                "The API client could not be revoked."));
        }
    }

    public async Task<Result<ApiClientVerification>> VerifyAsync(
        string clientId,
        string secret,
        string? remoteIpAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedClientId = clientId.Trim();
        var entity = await context.ApiClients
            .SingleOrDefaultAsync(client => client.ClientId == normalizedClientId, cancellationToken);
        if (entity is null || !string.Equals(entity.Status, ActiveStatus, StringComparison.Ordinal))
        {
            return InvalidCredential();
        }

        var now = clock.UtcNow;
        if (entity.ExpiresAtUtc.HasValue && entity.ExpiresAtUtc <= now)
        {
            return InvalidCredential();
        }

        var allowedIps = DeserializeStrings(entity.IpRestrictionsJson);
        if (allowedIps.Length > 0 &&
            (string.IsNullOrWhiteSpace(remoteIpAddress) || !allowedIps.Contains(remoteIpAddress.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            return InvalidCredential();
        }

        var currentMatch = ApiClientSecretHasher.Verify(secret, entity.SecretHash);
        var previousMatch = !currentMatch &&
                            entity.PreviousSecretValidUntilUtc > now &&
                            ApiClientSecretHasher.Verify(secret, entity.PreviousSecretHash ?? string.Empty);
        if (!currentMatch && !previousMatch)
        {
            return InvalidCredential();
        }

        entity.LastUsedAtUtc = now;
        entity.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);

        var scopes = DeserializeStrings(entity.ScopesJson).ToHashSet(StringComparer.Ordinal);
        var warehouseIds = DeserializeInts(entity.WarehouseIdsJson).ToHashSet();
        var contextValue = new ApiClientContext(
            entity.ClientId,
            entity.Name,
            scopes,
            warehouseIds,
            entity.HasGlobalWarehouseAccess);
        return Result.Success(new ApiClientVerification(ToDto(entity), contextValue));
    }

    private async Task<Result<WmsUser>> AuthorizeActorAsync(
        string actorUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Result.Failure<WmsUser>(WmsErrors.Unauthorized(
                "authorization.authentication_required",
                "An active administrator is required."));
        }

        var actor = await userManager.FindByIdAsync(actorUserId.Trim());
        if (actor is null || !actor.IsActive || !await userManager.IsInRoleAsync(actor, WmsRoleNames.Administrator))
        {
            return Result.Failure<WmsUser>(WmsErrors.Forbidden(
                "api_client.administrator_required",
                "Only an active administrator can manage API clients."));
        }

        return Result.Success(actor);
    }

    private async Task<Result<NormalizedApiClientRequest>> NormalizeAsync(
        ApiClientCreateRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var owner = request.Owner?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 200 || owner.Length is < 2 or > 450)
        {
            return Result.Failure<NormalizedApiClientRequest>(WmsErrors.Validation(
                "api_client.identity_invalid",
                "API client name and owner are required within their documented length limits."));
        }

        var scopes = (request.Scopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (scopes.Length == 0 || scopes.Any(scope => !ApiClientScopeCatalog.IsKnown(scope)))
        {
            return Result.Failure<NormalizedApiClientRequest>(WmsErrors.Validation(
                "api_client.scope_invalid",
                "Every API client scope must be selected from the published scope catalog."));
        }

        var warehouseIds = (request.WarehouseIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .Order()
            .ToArray();
        if (!request.HasGlobalWarehouseAccess && warehouseIds.Length == 0)
        {
            return Result.Failure<NormalizedApiClientRequest>(WmsErrors.Validation(
                "api_client.warehouse_scope_required",
                "An API client must have at least one warehouse restriction unless global access is explicitly selected."));
        }

        var activeWarehouseIds = await context.Warehouses
            .AsNoTracking()
            .Where(warehouse => warehouseIds.Contains(warehouse.Id) && warehouse.IsActive)
            .Select(warehouse => warehouse.Id)
            .ToHashSetAsync(cancellationToken);
        if (activeWarehouseIds.Count != warehouseIds.Length)
        {
            return Result.Failure<NormalizedApiClientRequest>(WmsErrors.NotFound(
                "api_client.warehouse_scope_invalid",
                "One or more selected warehouses are missing or inactive."));
        }

        var ipRestrictions = (request.IpRestrictions ?? [])
            .Where(ip => !string.IsNullOrWhiteSpace(ip))
            .Select(ip => ip.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ipRestrictions.Any(ip => ip.Length > 64))
        {
            return Result.Failure<NormalizedApiClientRequest>(WmsErrors.Validation(
                "api_client.ip_restriction_invalid",
                "Each API client IP restriction must be at most 64 characters."));
        }

        var now = clock.UtcNow;
        if (request.ExpiresAtUtc.HasValue && request.ExpiresAtUtc <= now)
        {
            return Result.Failure<NormalizedApiClientRequest>(WmsErrors.Validation(
                "api_client.expiry_invalid",
                "Credential expiry must be in the future."));
        }

        return Result.Success(new NormalizedApiClientRequest(
            name,
            owner,
            scopes,
            warehouseIds,
            request.HasGlobalWarehouseAccess,
            ipRestrictions,
            request.ExpiresAtUtc));
    }

    private static Result<ApiClientVerification> InvalidCredential() =>
        Result.Failure<ApiClientVerification>(WmsErrors.Unauthorized(
            "api_client.invalid_credentials",
            "The API client credentials are invalid, expired, revoked, or outside their network restriction."));

    private static ApiClientDto ToDto(WmsApiClientEntity entity) =>
        new(
            entity.ClientId,
            entity.Name,
            entity.Owner,
            entity.Status,
            DeserializeStrings(entity.ScopesJson).ToHashSet(StringComparer.Ordinal),
            DeserializeInts(entity.WarehouseIdsJson).ToHashSet(),
            entity.HasGlobalWarehouseAccess,
            DeserializeStrings(entity.IpRestrictionsJson),
            entity.SecretVersion,
            entity.CreatedAtUtc,
            entity.UpdatedAtUtc,
            entity.ExpiresAtUtc,
            entity.LastUsedAtUtc,
            entity.RevokedAtUtc,
            entity.RotatedAtUtc,
            entity.PreviousSecretValidUntilUtc);

    private static string NewClientId() => "wc_" + NewToken(18);

    private static string NewSecret() => "wcs_" + NewToken(32);

    private static string NewToken(int byteCount)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount));
        return value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Serialize<T>(IReadOnlyCollection<T> values) =>
        JsonSerializer.Serialize(values, JsonOptions);

    private static string[] DeserializeStrings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int[] DeserializeInts(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record NormalizedApiClientRequest(
        string Name,
        string Owner,
        IReadOnlyList<string> Scopes,
        IReadOnlyList<int> WarehouseIds,
        bool HasGlobalWarehouseAccess,
        IReadOnlyList<string> IpRestrictions,
        DateTimeOffset? ExpiresAtUtc);
}
