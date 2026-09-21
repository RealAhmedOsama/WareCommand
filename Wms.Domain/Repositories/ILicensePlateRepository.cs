using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Repositories;

#pragma warning disable CA1068
public interface ILicensePlateRepository : IRepository<LicensePlate>
{
    Task<LicensePlate?> GetByIdWithContentsAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<LicensePlate?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicensePlate>> SearchAsync(
        int? warehouseId,
        string? number,
        LicensePlateStatus? status,
        bool includeVoided,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicensePlateContent>> GetContentsAsync(
        int licensePlateId,
        CancellationToken cancellationToken = default);

    Task<LicensePlateContent?> FindContentAsync(
        int licensePlateId,
        int itemId,
        int? lotId,
        int? serialNumberId,
        int inventoryStatusId,
        int? itemPackagingId,
        CancellationToken cancellationToken = default,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null);

    Task<LicensePlateContent> AddContentAsync(
        LicensePlateContent content,
        CancellationToken cancellationToken = default);

    Task RemoveContentAsync(
        LicensePlateContent content,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicensePlate>> GetDescendantsAsync(
        LicensePlate licensePlate,
        CancellationToken cancellationToken = default);

    Task<int> CountAtLocationAsync(
        int locationId,
        int? excludingLicensePlateId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LicensePlateHistory>> GetHistoryAsync(
        int licensePlateId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<LicensePlateHistory> AddHistoryAsync(
        LicensePlateHistory history,
        CancellationToken cancellationToken = default);

    Task<LicensePlateNumberSequence?> GetNumberSequenceAsync(
        int warehouseId,
        CancellationToken cancellationToken = default);

    Task<LicensePlateNumberSequence> AddNumberSequenceAsync(
        LicensePlateNumberSequence sequence,
        CancellationToken cancellationToken = default);
}
#pragma warning restore CA1068
