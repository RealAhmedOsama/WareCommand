// Wms.Infrastructure/Data/Configurations/StockConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.Data.Configurations;

public class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("Stock");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SerialNumber)
            .HasMaxLength(100);

        builder.Property(e => e.SerialNumberId);

        builder.Property(e => e.InventoryStatusId)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Value object configurations
        builder.Property(e => e.QuantityAvailable)
            .HasConversion(
                v => v.Value,
                v => new Quantity(v))
            .HasColumnType("decimal(28,12)")
            .IsRequired();

        builder.Property(e => e.QuantityReserved)
            .HasConversion(
                v => v.Value,
                v => new Quantity(v))
            .HasColumnType("decimal(28,12)")
            .IsRequired();

        // Relationships
        builder.HasOne(e => e.Item)
            .WithMany()
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Location)
            .WithMany()
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Lot)
            .WithMany()
            .HasForeignKey(e => e.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Serial)
            .WithMany()
            .HasForeignKey(e => e.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InventoryStatus)
            .WithMany()
            .HasForeignKey(e => e.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => new
            {
                e.ItemId,
                e.LocationId,
                e.LotId,
                e.SerialNumber,
                e.InventoryStatusId
            })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(e => e.ItemId);
        builder.HasIndex(e => e.LocationId);
        builder.HasIndex(e => e.LotId);
        builder.HasIndex(e => e.SerialNumberId);
        builder.HasIndex(e => e.InventoryStatusId);
        builder.ToTable("Stock", table => table.HasCheckConstraint(
            "CK_Stock_SerialQuantity",
            "\"SerialNumberId\" IS NULL OR (\"QuantityAvailable\" >= 0 AND \"QuantityAvailable\" <= 1 AND \"QuantityReserved\" >= 0 AND \"QuantityReserved\" <= 1)"));
    }
}
