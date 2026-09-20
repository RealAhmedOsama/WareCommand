using Microsoft.EntityFrameworkCore;
using Wms.Application.Auditing;
using Wms.Application.Common;
using Wms.Application.Identity;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Auditing;

public sealed class AuditQueryService(
    WmsDbContext context,
    IWarehouseAccessService warehouseAccessService) : IAuditQueryService
{
    public async Task<Result<AuditPage>> SearchAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var authorization = await warehouseAccessService.AuthorizeAsync(
            WmsPermissions.AuditRead,
            cancellationToken: cancellationToken);
        if (authorization.IsFailure)
        {
            return Result.Failure<AuditPage>(authorization.Error);
        }

        var scope = await warehouseAccessService.GetScopeAsync(cancellationToken);
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var entries = context.AuditEntries.AsNoTracking().AsQueryable();

        if (query.FromUtc.HasValue)
        {
            var fromUnixMilliseconds = query.FromUtc.Value.ToUnixTimeMilliseconds();
            entries = entries.Where(entry => entry.OccurredAtUnixMilliseconds >= fromUnixMilliseconds);
        }

        if (query.ToUtc.HasValue)
        {
            var toUnixMilliseconds = query.ToUtc.Value.ToUnixTimeMilliseconds();
            entries = entries.Where(entry => entry.OccurredAtUnixMilliseconds <= toUnixMilliseconds);
        }

        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            entries = entries.Where(entry => entry.ActorUserId == query.UserId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(entry => entry.EntityType == query.EntityType);
        }

        if (query.WarehouseId.HasValue)
        {
            entries = scope.HasGlobalAccess || scope.WarehouseIds.Contains(query.WarehouseId.Value)
                ? entries.Where(entry => entry.WarehouseId == query.WarehouseId.Value)
                : entries.Where(_ => false);
        }
        else if (!scope.HasGlobalAccess)
        {
            entries = entries.Where(entry =>
                entry.WarehouseId == null || scope.WarehouseIds.Contains(entry.WarehouseId.Value));
        }

        var totalCount = await entries.CountAsync(cancellationToken);
        var items = await entries
            .OrderByDescending(entry => entry.OccurredAtUnixMilliseconds)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new AuditEntryDto(
                entry.Id,
                entry.OccurredAtUtc,
                entry.ActorUserId,
                entry.ActorUserName,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.WarehouseId,
                context.Warehouses
                    .Where(warehouse => warehouse.Id == entry.WarehouseId)
                    .Select(warehouse => warehouse.Code)
                    .FirstOrDefault(),
                entry.CorrelationId,
                entry.SourceClient,
                entry.Succeeded,
                entry.Details,
                entry.BeforeJson,
                entry.AfterJson))
            .ToListAsync(cancellationToken);

        return Result.Success(new AuditPage(items, page, pageSize, totalCount));
    }
}
