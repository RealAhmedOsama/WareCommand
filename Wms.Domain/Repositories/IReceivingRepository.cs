using Wms.Domain.Receiving;

namespace Wms.Domain.Repositories;

public interface IReceivingRepository
{
    Task<ReceivingTarget?> GetTargetAsync(
        string itemSku,
        string locationCode,
        CancellationToken cancellationToken = default);
}
