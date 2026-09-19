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

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Value object configurations
        builder.Property(e => e.QuantityAvailable)
            .HasConversion(
                v => v.Value,
                v => new Quantity(v))
            .HasColumnType("decimal(18,4)")
            .IsRequired();

        builder.Property(e => e.QuantityReserved)
            .HasConversion(
                v => v.Value,
                v => new Quantity(v))
            .HasColumnType("decimal(18,4)")
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

        // Indexes
        builder.HasIndex(e => new { e.ItemId, e.LocationId, e.LotId, e.SerialNumber })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.HasIndex(e => e.ItemId);
        builder.HasIndex(e => e.LocationId);
        builder.HasIndex(e => e.LotId);
    }
}
