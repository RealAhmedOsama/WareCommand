using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> builder)
    {
        builder.ToTable("SalesOrderLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.ItemSkuSnapshot).IsRequired().HasMaxLength(50);
        builder.Property(line => line.ItemNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(line => line.ItemLocalizedNameSnapshot).HasMaxLength(200);
        builder.Property(line => line.CustomerItemSkuSnapshot).HasMaxLength(100);
        builder.Property(line => line.OrderedUnitOfMeasure).IsRequired().HasMaxLength(20);
        builder.Property(line => line.OrderedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.BaseUnitOfMeasure).IsRequired().HasMaxLength(20);
        builder.Property(line => line.OrderedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.AllocatedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.PickedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.PackedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ShippedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.CancelledBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionFactorToBase).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionPrecision).IsRequired();
        builder.Property(line => line.ConversionPath).IsRequired().HasMaxLength(500);
        builder.Property(line => line.ConversionRuleIds).IsRequired().HasMaxLength(500);
        builder.Property(line => line.PackagingCodeSnapshot).HasMaxLength(100);
        builder.Property(line => line.PackagingUnitsPerPackageSnapshot).HasColumnType("decimal(28,12)");
        builder.Property(line => line.Notes).HasMaxLength(1_000);
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(line => line.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.SalesOrderId, line.LineNumber }).IsUnique();
        builder.HasIndex(line => new { line.ItemId, line.SalesOrderId });
    }
}
