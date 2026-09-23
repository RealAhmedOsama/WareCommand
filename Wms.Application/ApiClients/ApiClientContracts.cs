using Wms.Application.Common;
using Wms.Application.Identity;

namespace Wms.Application.ApiClients;

public static class ApiClientScopeCatalog
{
    public const string WebhooksRead = "webhooks.read";
    public const string WebhooksManage = "webhooks.manage";

    public static IReadOnlySet<string> Values { get; } = new HashSet<string>(
    [
        WmsPermissions.ItemsRead,
        WmsPermissions.ItemsManage,
        WmsPermissions.SuppliersRead,
        WmsPermissions.SuppliersManage,
        WmsPermissions.CustomersRead,
        WmsPermissions.CustomersManage,
        WmsPermissions.LocationsRead,
        WmsPermissions.LocationsManage,
        WmsPermissions.InventoryRead,
        WmsPermissions.InventoryAdjust,
        WmsPermissions.InventoryOwnershipRead,
        WmsPermissions.InventoryOwnershipManage,
        WmsPermissions.SalesOrdersRead,
        WmsPermissions.SalesOrdersManage,
        WmsPermissions.PurchaseOrdersRead,
        WmsPermissions.PurchaseOrdersManage,
        WmsPermissions.AdvanceShippingNoticesRead,
        WmsPermissions.AdvanceShippingNoticesManage,
        WmsPermissions.ReceiptsRead,
        WmsPermissions.ReceiptsManage,
        WmsPermissions.ReceivingExecute,
        WmsPermissions.ReceivingOverride,
        WmsPermissions.WorkRead,
        WmsPermissions.WorkExecute,
        WmsPermissions.WorkManage,
        WmsPermissions.PutawayExecute,
        WmsPermissions.PickingExecute,
        WmsPermissions.PackingExecute,
        WmsPermissions.ShippingExecute,
        WmsPermissions.AllocationManage,
        WmsPermissions.CountingExecute,
        WmsPermissions.ReportsRead,
        WmsPermissions.AttachmentsRead,
        WmsPermissions.NotificationsRead,
        WmsPermissions.WarehouseManage,
        WebhooksRead,
        WebhooksManage
    ],
    StringComparer.Ordinal);

    public static bool IsKnown(string scope) => Values.Contains(scope);
}

public static class ApiClientClaimTypes
{
    public const string ClientId = "warecommand/api-client-id";
    public const string Scope = "warecommand/api-scope";
    public const string WarehouseId = "warecommand/api-warehouse-id";
    public const string GlobalWarehouseAccess = "warecommand/api-global-warehouse-access";
}

public sealed record ApiClientContext(
    string ClientId,
    string Name,
    IReadOnlySet<string> Scopes,
    IReadOnlySet<int> WarehouseIds,
    bool HasGlobalWarehouseAccess)
{
    public bool HasScope(string permission) => Scopes.Contains(permission);

    public bool CanAccessWarehouse(int warehouseId) =>
        HasGlobalWarehouseAccess || WarehouseIds.Contains(warehouseId);
}

public interface IApiClientContextAccessor
{
    ApiClientContext? Current { get; set; }
}

public sealed class ApiClientContextAccessor : IApiClientContextAccessor
{
    public ApiClientContext? Current { get; set; }
}

public sealed record ApiClientDto(
    string ClientId,
    string Name,
    string Owner,
    string Status,
    IReadOnlySet<string> Scopes,
    IReadOnlySet<int> WarehouseIds,
    bool HasGlobalWarehouseAccess,
    IReadOnlyList<string> IpRestrictions,
    int SecretVersion,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? RotatedAtUtc,
    DateTimeOffset? PreviousSecretValidUntilUtc);

public sealed record ApiClientCreateRequest(
    string Name,
    string Owner,
    IReadOnlyCollection<string> Scopes,
    IReadOnlyCollection<int>? WarehouseIds = null,
    bool HasGlobalWarehouseAccess = false,
    IReadOnlyCollection<string>? IpRestrictions = null,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record ApiClientRotateRequest(
    TimeSpan Overlap = default,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record ApiClientIssue(
    ApiClientDto Client,
    string Secret);

public sealed record ApiClientVerification(
    ApiClientDto Client,
    ApiClientContext Context);

public interface IApiClientCredentialService
{
    Task<Result<ApiClientIssue>> CreateAsync(
        ApiClientCreateRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ApiClientDto>>> ListAsync(
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ApiClientIssue>> RotateAsync(
        string clientId,
        ApiClientRotateRequest request,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ApiClientDto>> RevokeAsync(
        string clientId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<Result<ApiClientVerification>> VerifyAsync(
        string clientId,
        string secret,
        string? remoteIpAddress,
        CancellationToken cancellationToken = default);
}
