using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Wms.Application.Context;
using Wms.Domain.Entities;
using Wms.Infrastructure.Data;

namespace Wms.Infrastructure.Receiving;

public sealed class WarehouseReceiptNumberAllocator(
    IClock clock,
    IDbContextFactory<WmsDbContext> contextFactory)
{
    public async Task<long> AllocateAsync(
        int warehouseId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
        {
            var postgresNumber = await TryAllocateExistingPostgreSqlSequenceAsync(
                context,
                warehouseId,
                cancellationToken);
            if (postgresNumber.HasValue)
            {
                return postgresNumber.Value;
            }
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var updatedAt = clock.UtcNow.UtcDateTime;
        var rowsUpdated = await context.WarehouseNumberSequences
            .Where(sequence => sequence.WarehouseId == warehouseId &&
                               sequence.NextReceiptNumber < long.MaxValue)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(sequence => sequence.NextReceiptNumber, sequence => sequence.NextReceiptNumber + 1)
                .SetProperty(sequence => sequence.Revision, sequence => sequence.Revision + 1)
                .SetProperty(sequence => sequence.UpdatedAt, updatedAt),
                cancellationToken);

        long allocatedNumber;
        if (rowsUpdated == 1)
        {
            var nextNumber = await context.WarehouseNumberSequences.AsNoTracking()
                .Where(sequence => sequence.WarehouseId == warehouseId)
                .Select(sequence => sequence.NextReceiptNumber)
                .SingleAsync(cancellationToken);
            allocatedNumber = nextNumber - 1;
        }
        else
        {
            var currentNumber = await context.WarehouseNumberSequences.AsNoTracking()
                .Where(sequence => sequence.WarehouseId == warehouseId)
                .Select(sequence => (long?)sequence.NextReceiptNumber)
                .SingleOrDefaultAsync(cancellationToken);
            if (currentNumber.HasValue)
            {
                throw new InvalidOperationException("The warehouse receipt number sequence is exhausted.");
            }

            var sequence = new WarehouseNumberSequence(warehouseId);
            allocatedNumber = sequence.AllocateReceiptNumber();
            context.WarehouseNumberSequences.Add(sequence);
            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return allocatedNumber;
    }

    private async Task<long?> TryAllocateExistingPostgreSqlSequenceAsync(
        WmsDbContext context,
        int warehouseId,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            UPDATE "WarehouseNumberSequences"
            SET "NextReceiptNumber" = "NextReceiptNumber" + 1,
                "Revision" = "Revision" + 1,
                "UpdatedAt" = @updatedAt
            WHERE "WarehouseId" = @warehouseId
              AND "NextReceiptNumber" < @maximum
            RETURNING "NextReceiptNumber" - 1;
            """;
        command.Parameters.Add(new NpgsqlParameter("updatedAt", NpgsqlDbType.TimestampTz)
        {
            Value = clock.UtcNow.UtcDateTime
        });
        command.Parameters.Add(new NpgsqlParameter("warehouseId", NpgsqlDbType.Integer)
        {
            Value = warehouseId
        });
        command.Parameters.Add(new NpgsqlParameter("maximum", NpgsqlDbType.Bigint)
        {
            Value = long.MaxValue
        });

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
