using Wms.Application.Common;
using Wms.Application.Identity;

namespace Wms.Application.Tests.Identity;

public sealed class AllowAllWarehouseAccessService : IWarehouseAccessService
{
    public Task<bool> HasPermissionAsync(
        string permission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<Result> AuthorizeAsync(
        string permission,
        int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<WarehouseAccessScope> GetScopeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WarehouseAccessScope(true, new HashSet<int>()));

    public Task<IReadOnlyList<WmsWarehouseOption>> GetAccessibleWarehousesAsync(
        string permission,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WmsWarehouseOption>>([]);
}
