using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Repositories;

public interface ILotRepository : IRepository<Lot>
{
    Task<Lot?> GetByItemAndNumberAsync(
        int itemId,
        string number,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Lot>> GetByItemIdAsync(
        int itemId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Lot>> GetByStatusAsync(
        LotStatus status,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<Lot>> GetExpiringAsync(
        DateOnly throughDate,
        CancellationToken cancellationToken = default);

    Task<Lot> GetOrCreateAsync(
        int itemId,
        string number,
        DateTime? expiryDate,
        DateTime? manufacturedDate,
        DateTime? retestDate,
        DateTime? holdUntil,
        string? supplierLotNumber,
        string? notes,
        LotStatus status,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default);
}
