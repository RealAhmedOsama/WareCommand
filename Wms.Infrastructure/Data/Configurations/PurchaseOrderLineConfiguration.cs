using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> builder)
    {
        builder.ToTable("PurchaseOrderLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.ItemSkuSnapshot)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(line => line.ItemNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(line => line.OrderedUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(line => line.OrderedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(line => line.BaseUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(line => line.OrderedBaseQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(line => line.ReceivedBaseQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(line => line.ConversionFactorToBase)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(line => line.ConversionPrecision).IsRequired();
        builder.Property(line => line.ConversionRoundingMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(line => line.ConversionRoundingDelta)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(line => line.ConversionPath)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(line => line.ConversionRuleIds)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(line => line.OverDeliveryTolerancePercent)
            .HasColumnType("decimal(18,4)")
            .IsRequired();
        builder.Property(line => line.UnderDeliveryTolerancePercent)
            .HasColumnType("decimal(18,4)")
            .IsRequired();
        builder.Property(line => line.SupplierItemReferenceSnapshot)
            .HasMaxLength(100);
        builder.Property(line => line.Notes)
            .HasMaxLength(1_000);
        builder.Property(line => line.IsClosed).IsRequired();
        builder.Property(line => line.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(line => line.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(line => line.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.PurchaseOrderId, line.LineNumber }).IsUnique();
        builder.HasIndex(line => new { line.ItemId, line.PurchaseOrderId });
    }
}
