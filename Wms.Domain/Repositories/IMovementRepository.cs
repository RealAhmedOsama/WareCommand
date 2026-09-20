// Wms.Domain/Repositories/IMovementRepository.cs

using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Repositories;

public interface IMovementRepository : IRepository<Movement>
{
    Task<IEnumerable<Movement>> GetByItemIdAsync(int itemId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Movement>> GetByLocationIdAsync(int locationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns movements in the half-open UTC interval [fromInclusiveUtc, toExclusiveUtc).
    /// </summary>
    Task<IEnumerable<Movement>> GetByDateRangeAsync(DateTime fromInclusiveUtc, DateTime toExclusiveUtc,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Movement>> GetByTypeAsync(MovementType type, CancellationToken cancellationToken = default);
    Task<IEnumerable<Movement>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
}
