using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Wms.Domain.Entities;
using Wms.Domain.Enums;
using Wms.Domain.Repositories;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public sealed class LotRepository : Repository<Lot>, ILotRepository
{
    private readonly WmsDbContext _context;

    public LotRepository(WmsDbContext context) : base(context)
    {
        _context = context;
    }

    public override async Task<Lot?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(lot => lot.Item)
            .FirstOrDefaultAsync(lot => lot.Id == id, cancellationToken);

    public override async Task<IEnumerable<Lot>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(lot => lot.Item)
            .OrderBy(lot => lot.ItemId)
            .ThenBy(lot => lot.Number)
            .ToListAsync(cancellationToken);

    public async Task<Lot?> GetByItemAndNumberAsync(
        int itemId,
        string number,
        CancellationToken cancellationToken = default)
    {
        var normalizedNumber = Lot.NormalizeNumber(number);
        return await DbSet
            .Include(lot => lot.Item)
            .FirstOrDefaultAsync(
                lot => lot.ItemId == itemId && lot.Number == normalizedNumber,
                cancellationToken);
    }

    public async Task<IEnumerable<Lot>> GetByItemIdAsync(
        int itemId,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(lot => lot.Item)
            .Where(lot => lot.ItemId == itemId)
            .OrderBy(lot => lot.ExpiryDate)
            .ThenBy(lot => lot.Number)
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<Lot>> GetByStatusAsync(
        LotStatus status,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .Include(lot => lot.Item)
            .Where(lot => lot.Status == status)
            .OrderBy(lot => lot.ExpiryDate)
            .ThenBy(lot => lot.Number)
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<Lot>> GetExpiringAsync(
        DateOnly throughDate,
        CancellationToken cancellationToken = default)
    {
        var boundary = throughDate.ToDateTime(TimeOnly.MinValue);
        return await DbSet
            .Include(lot => lot.Item)
            .Where(lot => lot.IsActive &&
                         lot.ExpiryDate.HasValue &&
                         lot.ExpiryDate.Value <= boundary &&
                         lot.Status != LotStatus.Closed)
            .OrderBy(lot => lot.ExpiryDate)
            .ThenBy(lot => lot.Number)
            .ToListAsync(cancellationToken);
    }

    public async Task<Lot> GetOrCreateAsync(
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
        CancellationToken cancellationToken = default)
    {
        var normalizedNumber = Lot.NormalizeNumber(number);
        var tracked = DbSet.Local.FirstOrDefault(lot =>
            lot.ItemId == itemId && lot.Number == normalizedNumber);
        if (tracked is not null)
        {
            return tracked;
        }

        if (!_context.Database.IsRelational())
        {
            var existing = await GetByItemAndNumberAsync(itemId, normalizedNumber, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var inMemoryLot = new Lot(
                normalizedNumber,
                itemId,
                expiryDate,
                manufacturedDate,
                retestDate,
                holdUntil,
                supplierLotNumber,
                notes,
                status);
            await AddAsync(inMemoryLot, cancellationToken);
            return inMemoryLot;
        }

        var existingLot = await GetByItemAndNumberAsync(itemId, normalizedNumber, cancellationToken);
        if (existingLot is not null)
        {
            return existingLot;
        }

        var normalizedExpiryDate = NormalizeDateOnly(expiryDate);
        var normalizedManufacturedDate = NormalizeDateOnly(manufacturedDate);
        var normalizedRetestDate = NormalizeDateOnly(retestDate);
        var normalizedHoldUntil = NormalizeDateOnly(holdUntil);
        var normalizedSupplierLotNumber = NormalizeOptional(supplierLotNumber, 100);
        var normalizedNotes = NormalizeOptional(notes, 1_000);
        var normalizedCreatedAtUtc = createdAtUtc.Kind == DateTimeKind.Utc
            ? createdAtUtc
            : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);

        if (_context.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true)
        {
            await _context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Lots" ("Number", "ItemId", "ExpiryDate", "ManufacturedDate", "RetestDate", "HoldUntil", "SupplierLotNumber", "Notes", "Status", "IsActive", "RecallReason", "RecalledAt", "CreatedAt", "UpdatedAt")
                VALUES (@number, @itemId, @expiryDate, @manufacturedDate, @retestDate, @holdUntil, @supplierLotNumber, @notes, @status, @isActive, NULL, NULL, @createdAt, @updatedAt)
                ON CONFLICT ("ItemId", "Number") DO NOTHING
                """,
                [
                    new NpgsqlParameter("number", NpgsqlDbType.Text) { Value = normalizedNumber },
                    new NpgsqlParameter("itemId", NpgsqlDbType.Integer) { Value = itemId },
                    new NpgsqlParameter("expiryDate", NpgsqlDbType.Timestamp) { Value = (object?)normalizedExpiryDate ?? DBNull.Value },
                    new NpgsqlParameter("manufacturedDate", NpgsqlDbType.Timestamp) { Value = (object?)normalizedManufacturedDate ?? DBNull.Value },
                    new NpgsqlParameter("retestDate", NpgsqlDbType.Timestamp) { Value = (object?)normalizedRetestDate ?? DBNull.Value },
                    new NpgsqlParameter("holdUntil", NpgsqlDbType.Timestamp) { Value = (object?)normalizedHoldUntil ?? DBNull.Value },
                    new NpgsqlParameter("supplierLotNumber", NpgsqlDbType.Text) { Value = (object?)normalizedSupplierLotNumber ?? DBNull.Value },
                    new NpgsqlParameter("notes", NpgsqlDbType.Text) { Value = (object?)normalizedNotes ?? DBNull.Value },
                    new NpgsqlParameter("status", NpgsqlDbType.Integer) { Value = (int)status },
                    new NpgsqlParameter("isActive", NpgsqlDbType.Boolean) { Value = status != LotStatus.Closed },
                    new NpgsqlParameter("createdAt", NpgsqlDbType.TimestampTz) { Value = normalizedCreatedAtUtc },
                    new NpgsqlParameter("updatedAt", NpgsqlDbType.TimestampTz) { Value = normalizedCreatedAtUtc }
                ],
                cancellationToken);
        }
        else
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO "Lots" ("Number", "ItemId", "ExpiryDate", "ManufacturedDate", "RetestDate", "HoldUntil", "SupplierLotNumber", "Notes", "Status", "IsActive", "RecallReason", "RecalledAt", "CreatedAt", "UpdatedAt")
                VALUES ({normalizedNumber}, {itemId}, {normalizedExpiryDate}, {normalizedManufacturedDate}, {normalizedRetestDate}, {normalizedHoldUntil}, {normalizedSupplierLotNumber}, {normalizedNotes}, {(int)status}, {status != LotStatus.Closed}, {null}, {null}, {normalizedCreatedAtUtc}, {normalizedCreatedAtUtc})
                """, cancellationToken);
        }

        return await GetByItemAndNumberAsync(itemId, normalizedNumber, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Lot '{normalizedNumber}' for item {itemId} could not be loaded after creation.");
    }

    private static DateTime? NormalizeDateOnly(DateTime? value) =>
        value.HasValue
            ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Unspecified)
            : null;

    private static string? NormalizeOptional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maximumLength)];
}
