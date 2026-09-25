namespace Wms.Application.Identity;

public sealed record WarehouseNavigationAccess(
    IReadOnlySet<string> Permissions,
    IReadOnlyList<WmsWarehouseOption> AccessibleWarehouses,
    bool HasWildcardPermissions)
{
    public static WarehouseNavigationAccess Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        Array.Empty<WmsWarehouseOption>(),
        HasWildcardPermissions: true);

    public bool HasPermission(string permission) =>
        (HasWildcardPermissions && Permissions.Contains(WmsPermissions.All)) ||
        Permissions.Contains(permission);
}

public interface IWarehouseNavigationAccessService
{
    Task<WarehouseNavigationAccess> GetNavigationAccessAsync(
        CancellationToken cancellationToken = default);
}
