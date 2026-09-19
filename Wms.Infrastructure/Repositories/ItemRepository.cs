// Wms.Infrastructure/Repositories/ItemRepository.cs

using Microsoft.EntityFrameworkCore;
using Wms.Domain.Entities;
using Wms.Domain.Repositories;
using Wms.Domain.ValueObjects;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Repositories;

public class ItemRepository : Repository<Item>, IItemRepository
{
    private const string PostgreSqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private readonly WmsDbContext _context;

    public ItemRepository(WmsDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<Item?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var normalizedSku = sku.ToUpperInvariant();
        return await DbSet
            .FirstOrDefaultAsync(i => i.Sku == normalizedSku, cancellationToken);
    }

    public async Task<Item?> GetByBarcodeAsync(Barcode barcode, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(i => i.Barcodes.Any(b => b.Value == barcode.Value))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Item>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        var pattern = $"%{searchTerm}%";
        var query = DbSet.AsQueryable();

        if (string.Equals(_context.Database.ProviderName, PostgreSqlProviderName, StringComparison.Ordinal))
        {
            query = query.Where(i => EF.Functions.ILike(i.Sku, pattern) ||
                                     EF.Functions.ILike(i.Name, pattern) ||
                                     i.Barcodes.Any(b => EF.Functions.ILike(b.Value, pattern)));
        }
        else
        {
            query = query.Where(i => EF.Functions.Like(i.Sku, pattern) ||
                                     EF.Functions.Like(i.Name, pattern) ||
                                     i.Barcodes.Any(b => EF.Functions.Like(b.Value, pattern)));
        }

        return await query
            .OrderBy(i => i.Sku)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default)
    {
        var normalizedSku = sku.ToUpperInvariant();
        return await DbSet
            .AnyAsync(i => i.Sku == normalizedSku, cancellationToken);
    }

    public override async Task<IEnumerable<Item>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .OrderBy(i => i.Sku)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Item>> GetActiveItemsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(i => i.IsActive)
            .OrderBy(i => i.Sku)
            .ToListAsync(cancellationToken);
    }
}
