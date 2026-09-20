using System.Data;
using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class SerialNumberRepository(WmsDbContext context)
    : Repository<SerialNumber>(context), ISerialNumberRepository
{
    private readonly WmsDbContext _context = context;

    public override async Task<SerialNumber?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        return await IncludeNavigations(DbSet)
            .FirstOrDefaultAsync(serial => serial.Id == id, cancellationToken);
    }

    public async Task<SerialNumber?> GetByItemAndNumberAsync(
        int itemId,
        string number,
        CancellationToken cancellationToken = default)
    {
        var normalized = SerialNumber.NormalizeNumber(number);
        return await IncludeNavigations(DbSet)
            .FirstOrDefaultAsync(
                serial => serial.ItemId == itemId && serial.Number == normalized,
                cancellationToken);
    }

    public async Task<IEnumerable<SerialNumber>> GetByItemIdAsync(
        int itemId,
        CancellationToken cancellationToken = default)
    {
        return await IncludeNavigations(DbSet)
            .Where(serial => serial.ItemId == itemId)
            .OrderBy(serial => serial.Number)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<SerialNumber>> GetByStatusAsync(
        SerialStatus status,
        CancellationToken cancellationToken = default)
    {
        return await IncludeNavigations(DbSet)
            .Where(serial => serial.Status == status)
            .OrderBy(serial => serial.Number)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<SerialNumber>> SearchAsync(
        int? itemId,
        string? number,
        SerialStatus? status,
        bool includeMigrationConflicts,
        CancellationToken cancellationToken = default)
    {
        var query = IncludeNavigations(DbSet.AsQueryable());
        if (itemId.HasValue)
        {
            query = query.Where(serial => serial.ItemId == itemId.Value);
        }

        if (!string.IsNullOrWhiteSpace(number))
        {
            var normalized = number.Trim().ToUpperInvariant();
            query = query.Where(serial => serial.Number.Contains(normalized));
        }

        if (status.HasValue)
        {
            query = query.Where(serial => serial.Status == status.Value);
        }

        if (!includeMigrationConflicts)
        {
            query = query.Where(serial => !serial.HasMigrationConflict);
        }

        return await query
            .OrderBy(serial => serial.Number)
            .ToListAsync(cancellationToken);
    }

    public async Task<SerialNumber> GetOrCreateAsync(
        int itemId,
        string number,
        int? lotId,
        DateTime createdAtUtc,
        CancellationToken cancellationToken = default)
    {
        var normalized = SerialNumber.NormalizeNumber(number);
        var tracked = _context.ChangeTracker
            .Entries<SerialNumber>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(serial => serial.ItemId == itemId && serial.Number == normalized);
        if (tracked is not null)
        {
            return tracked;
        }

        var existing = await GetByItemAndNumberAsync(itemId, normalized, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var normalizedCreatedAt = createdAtUtc.Kind == DateTimeKind.Utc
            ? createdAtUtc
            : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
        if (!_context.Database.IsRelational())
        {
            var serial = new SerialNumber(normalized, itemId, lotId);
            await AddAsync(serial, cancellationToken);
            return serial;
        }

        var providerName = _context.Database.ProviderName ?? string.Empty;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "SerialNumbers" ("Number", "ItemId", "LotId", "Status", "HasMigrationConflict", "CreatedAt")
                VALUES ({normalized}, {itemId}, {lotId}, {(int)SerialStatus.Available}, {false}, {normalizedCreatedAt})
                ON CONFLICT ("ItemId", "Number") DO NOTHING;
                """, cancellationToken);
        }
        else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO "SerialNumbers" ("Number", "ItemId", "LotId", "Status", "HasMigrationConflict", "CreatedAt")
                VALUES ({normalized}, {itemId}, {lotId}, {(int)SerialStatus.Available}, {false}, {normalizedCreatedAt});
                """, cancellationToken);
        }
        else
        {
            throw new NotSupportedException(
                $"Atomic serial creation is not configured for provider '{providerName}'.");
        }

        var result = await GetByItemAndNumberAsync(itemId, normalized, cancellationToken);
        return result ?? throw new InvalidOperationException(
            $"Serial '{normalized}' could not be loaded after atomic creation.");
    }

    private static IQueryable<SerialNumber> IncludeNavigations(IQueryable<SerialNumber> query)
    {
        return query
            .Include(serial => serial.Item)
            .Include(serial => serial.Lot)
            .Include(serial => serial.CurrentWarehouse)
            .Include(serial => serial.CurrentLocation);
    }
}
