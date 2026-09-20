using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Repositories;

public interface ISerialNumberRepository : IRepository<SerialNumber>
{
    Task<SerialNumber?> GetByItemAndNumberAsync(
        int itemId,
        string number,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<SerialNumber>> GetByItemIdAsync(
        int itemId,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<SerialNumber>> GetByStatusAsync(
        SerialStatus status,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<SerialNumber>> SearchAsync(
        int? itemId,
        string? number,
        SerialStatus? status,
        bool includeMigrationConflicts,
        CancellationToken cancellationToken = default);

    Task<SerialNumber> GetOrCreateAsync(
        int itemId,
        string number,
        int? lotId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default);
}
