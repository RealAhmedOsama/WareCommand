using Wms.Application.Common;

namespace Wms.Infrastructure.Identity;

public interface IUserAccessDirectory
{
    Task<WmsUserAccessProfile?> GetProfileAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        string actorUserId,
        string targetUserId,
        IReadOnlyCollection<string> roleNames,
        IReadOnlyCollection<string> permissionNames,
        IReadOnlyCollection<int> warehouseIds,
        int? defaultWarehouseId,
        CancellationToken cancellationToken = default);
}

public sealed record WmsUserAccessProfile(
    string UserId,
    string UserName,
    IReadOnlyList<WmsAccessRoleOption> Roles,
    IReadOnlyList<WmsAccessPermissionOption> Permissions,
    IReadOnlyList<WmsAccessWarehouseOption> Warehouses);

public sealed record WmsAccessRoleOption(
    string Name,
    bool IsSelected);

public sealed record WmsAccessPermissionOption(
    string Name,
    string Description,
    bool IsDirectGrant,
    bool IsGrantedByRole);

public sealed record WmsAccessWarehouseOption(
    int Id,
    string Code,
    string Name,
    bool IsSelected,
    bool IsDefault);
