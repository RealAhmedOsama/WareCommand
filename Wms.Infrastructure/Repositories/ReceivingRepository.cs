using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Receiving;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class ReceivingRepository(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService) : IReceivingRepository
{
    public async Task<ReceivingTarget?> GetTargetAsync(
        string itemSku,
        string locationCode,
        CancellationToken cancellationToken = default)
    {
        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var normalizedSku = itemSku.ToUpperInvariant();
        var normalizedLocationCode = locationCode.Trim().ToUpperInvariant();
        var visibleLocations = context.Locations
            .Where(location => location.Code == normalizedLocationCode);
        if (!scope.HasGlobalAccess)
        {
            visibleLocations = visibleLocations
                .Where(location => scope.WarehouseIds.Contains(location.WarehouseId));
        }

        var target = await (
            from item in context.Items
            where item.Sku == normalizedSku
            join location in visibleLocations on 1 equals 1 into matchingLocations
            from location in matchingLocations.DefaultIfEmpty()
            orderby location == null ? 0 : location.Id
            select new { Item = item, Location = location })
            .TagWith("ReceivingRepository.GetTarget")
            .FirstOrDefaultAsync(cancellationToken);

        return target is null
            ? null
            : new ReceivingTarget(target.Item, target.Location);
    }
}
