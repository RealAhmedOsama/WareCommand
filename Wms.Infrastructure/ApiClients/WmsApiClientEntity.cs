namespace Wms.Infrastructure.ApiClients;

public sealed class WmsApiClientEntity
{
    public int Id { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Owner { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string ScopesJson { get; set; } = "[]";

    public string WarehouseIdsJson { get; set; } = "[]";

    public bool HasGlobalWarehouseAccess { get; set; }

    public string IpRestrictionsJson { get; set; } = "[]";

    public string SecretHash { get; set; } = string.Empty;

    public string? PreviousSecretHash { get; set; }

    public int SecretVersion { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public DateTimeOffset? RotatedAtUtc { get; set; }

    public DateTimeOffset? PreviousSecretValidUntilUtc { get; set; }
}
