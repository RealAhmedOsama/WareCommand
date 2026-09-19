// Wms.Domain/Repositories/IMovementRepository.cs

using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Repositories;

public interface IMovementRepository : IRepository<Movement>
{
    Task<IEnumerable<Movement>> GetByItemIdAsync(int itemId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Movement>> GetByLocationIdAsync(int locationId, CancellationToken cancellationToken = default);

    Task<IEnumerable<Movement>> GetByDateRangeAsync(DateTime from, DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Movement>> GetByTypeAsync(MovementType type, CancellationToken cancellationToken = default);
    Task<IEnumerable<Movement>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}
