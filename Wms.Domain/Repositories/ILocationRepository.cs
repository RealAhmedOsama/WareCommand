// Wms.Domain/Repositories/ILocationRepository.cs

using Wms.Domain.Entities;

namespace Wms.Domain.Repositories;

public interface ILocationRepository : IRepository<Location>
{
    Task<Location?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<Location?> GetByWarehouseAndCodeAsync(
        int warehouseId,
        string code,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<Location>> GetByWarehouseIdAsync(int warehouseId, CancellationToken cancellationToken = default);

    Task<IEnumerable<Location>> GetChildLocationsAsync(int parentLocationId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Location>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default);
    Task<IEnumerable<Location>> GetReceivableLocationsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Location>> GetPickableLocationsAsync(CancellationToken cancellationToken = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);
}
