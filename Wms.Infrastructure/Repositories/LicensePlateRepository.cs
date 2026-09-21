using Microsoft.EntityFrameworkCore;
using Wms.Application.Identity;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Inventory;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class LicensePlateRepository : Repository<LicensePlate>, ILicensePlateRepository
{
    private readonly WmsDbContext _context;
    private readonly IWarehouseAccessService _warehouseAccessService;

    public LicensePlateRepository(
        WmsDbContext context,
        IWarehouseAccessService warehouseAccessService) : base(context)
    {
        _context = context;
        _warehouseAccessService = warehouseAccessService;
    }

    public override async Task<LicensePlate?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await GetByIdWithContentsAsync(id, cancellationToken);
    }

    public async Task<LicensePlate?> GetByIdWithContentsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeGraph(DbSet.AsQueryable());
        query = ApplyScope(query, scope);
        return await query.FirstOrDefaultAsync(plate => plate.Id == id, cancellationToken);
    }

    public async Task<LicensePlate?> GetByNumberAsync(
        string number,
        CancellationToken cancellationToken = default)
    {
        var normalized = number.Trim().ToUpperInvariant();
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(DbSet.AsQueryable(), scope);
        return await query.FirstOrDefaultAsync(plate => plate.Number == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<LicensePlate>> SearchAsync(
        int? warehouseId,
        string? number,
        LicensePlateStatus? status,
        bool includeVoided,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(IncludeGraph(DbSet.AsQueryable()), scope);
        if (warehouseId.HasValue)
        {
            query = query.Where(plate => plate.WarehouseId == warehouseId.Value);
        }

        if (!string.IsNullOrWhiteSpace(number))
        {
            var normalized = number.Trim().ToUpperInvariant();
            query = query.Where(plate => plate.Number.Contains(normalized));
        }

        if (status.HasValue)
        {
            query = query.Where(plate => plate.Status == status.Value);
        }
        else if (!includeVoided)
        {
            query = query.Where(plate => plate.Status != LicensePlateStatus.Voided);
        }

        return await query
            .OrderBy(plate => plate.Number)
            .ThenBy(plate => plate.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LicensePlateContent>> GetContentsAsync(
        int licensePlateId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeContentGraph(_context.LicensePlateContents.AsQueryable())
            .Where(content => content.LicensePlateId == licensePlateId);
        query = scope.HasGlobalAccess
            ? query
            : query.Where(content => scope.WarehouseIds.Contains(content.LicensePlate.WarehouseId));
        return await query.OrderBy(content => content.ItemId).ThenBy(content => content.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<LicensePlateContent?> FindContentAsync(
        int licensePlateId,
        int itemId,
        int? lotId,
        int? serialNumberId,
        int inventoryStatusId,
        int? itemPackagingId,
        CancellationToken cancellationToken = default,
        InventoryOwnerKind ownerKind = InventoryOwnerKind.CompanyOwned,
        int? inventoryOwnerId = null,
        string? ownerCodeSnapshot = null)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = IncludeContentGraph(_context.LicensePlateContents.AsQueryable())
            .Where(content => content.LicensePlateId == licensePlateId &&
                              content.ItemId == itemId &&
                              content.LotId == lotId &&
                              content.SerialNumberId == serialNumberId &&
                              content.InventoryStatusId == inventoryStatusId &&
                              content.ItemPackagingId == itemPackagingId &&
                              content.OwnerKind == ownerKind &&
                              content.InventoryOwnerId == inventoryOwnerId &&
                              content.OwnerCodeSnapshot == InventoryOwnershipDimension.NormalizeOwnerCode(
                                  ownerKind,
                                  inventoryOwnerId,
                                  ownerCodeSnapshot));
        query = scope.HasGlobalAccess
            ? query
            : query.Where(content => scope.WarehouseIds.Contains(content.LicensePlate.WarehouseId));
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<LicensePlateContent> AddContentAsync(
        LicensePlateContent content,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.LicensePlateContents.AddAsync(content, cancellationToken);
        return entry.Entity;
    }

    public Task RemoveContentAsync(
        LicensePlateContent content,
        CancellationToken cancellationToken = default)
    {
        _context.LicensePlateContents.Remove(content);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<LicensePlate>> GetDescendantsAsync(
        LicensePlate licensePlate,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(
            DbSet.Where(plate => plate.WarehouseId == licensePlate.WarehouseId),
            scope);
        var all = await query.ToListAsync(cancellationToken);
        var descendants = new List<LicensePlate>();
        var frontier = new HashSet<int> { licensePlate.Id };
        var visited = new HashSet<int> { licensePlate.Id };
        while (frontier.Count > 0)
        {
            var children = all
                .Where(plate => plate.ParentLicensePlateId.HasValue &&
                                frontier.Contains(plate.ParentLicensePlateId.Value) &&
                                visited.Add(plate.Id))
                .ToArray();
            if (children.Length == 0)
            {
                break;
            }

            descendants.AddRange(children);
            frontier = children.Select(plate => plate.Id).ToHashSet();
        }

        return descendants;
    }

    public async Task<int> CountAtLocationAsync(
        int locationId,
        int? excludingLicensePlateId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = ApplyScope(
            DbSet.AsQueryable().Where(plate =>
                plate.CurrentLocationId == locationId &&
                plate.Status != LicensePlateStatus.Voided),
            scope);
        if (excludingLicensePlateId.HasValue)
        {
            query = query.Where(plate => plate.Id != excludingLicensePlateId.Value);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LicensePlateHistory>> GetHistoryAsync(
        int licensePlateId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        var query = _context.LicensePlateHistories
            .Include(history => history.Item)
            .Include(history => history.Lot)
            .Include(history => history.SerialNumber)
            .Where(history => history.LicensePlateId == licensePlateId);
        query = scope.HasGlobalAccess
            ? query
            : query.Where(history => scope.WarehouseIds.Contains(history.LicensePlate.WarehouseId));
        return await query
            .OrderByDescending(history => history.OccurredAtUtc)
            .ThenByDescending(history => history.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<LicensePlateHistory> AddHistoryAsync(
        LicensePlateHistory history,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.LicensePlateHistories.AddAsync(history, cancellationToken);
        return entry.Entity;
    }

    public async Task<LicensePlateNumberSequence?> GetNumberSequenceAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        var scope = await _warehouseAccessService.GetScopeAsync(cancellationToken);
        if (!scope.HasGlobalAccess && !scope.WarehouseIds.Contains(warehouseId))
        {
            return null;
        }

        return await _context.LicensePlateNumberSequences
            .FirstOrDefaultAsync(sequence => sequence.WarehouseId == warehouseId, cancellationToken);
    }

    public async Task<LicensePlateNumberSequence> AddNumberSequenceAsync(
        LicensePlateNumberSequence sequence,
        CancellationToken cancellationToken = default)
    {
        var entry = await _context.LicensePlateNumberSequences.AddAsync(sequence, cancellationToken);
        return entry.Entity;
    }

    private static IQueryable<LicensePlate> IncludeGraph(IQueryable<LicensePlate> query)
    {
        return query
            .Include(plate => plate.Warehouse)
            .Include(plate => plate.CurrentLocation)
            .Include(plate => plate.ParentLicensePlate)
            .Include(plate => plate.ChildLicensePlates)
            .Include(plate => plate.Contents)
                .ThenInclude(content => content.Item)
            .Include(plate => plate.Contents)
                .ThenInclude(content => content.Lot)
            .Include(plate => plate.Contents)
                .ThenInclude(content => content.SerialNumber)
            .Include(plate => plate.Contents)
                .ThenInclude(content => content.InventoryStatus)
            .Include(plate => plate.Contents)
                .ThenInclude(content => content.ItemPackaging)
            .AsSplitQuery();
    }

    private static IQueryable<LicensePlateContent> IncludeContentGraph(
        IQueryable<LicensePlateContent> query)
    {
        return query
            .Include(content => content.LicensePlate)
            .Include(content => content.Item)
            .Include(content => content.Lot)
            .Include(content => content.SerialNumber)
            .Include(content => content.InventoryStatus)
            .Include(content => content.ItemPackaging);
    }

    private static IQueryable<LicensePlate> ApplyScope(
        IQueryable<LicensePlate> query,
        WarehouseAccessScope scope)
    {
        return scope.HasGlobalAccess
            ? query
            : query.Where(plate => scope.WarehouseIds.Contains(plate.WarehouseId));
    }
}
