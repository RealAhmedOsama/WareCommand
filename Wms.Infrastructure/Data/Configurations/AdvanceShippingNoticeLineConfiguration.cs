using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class AdvanceShippingNoticeLineConfiguration : IEntityTypeConfiguration<AdvanceShippingNoticeLine>
{
    public void Configure(EntityTypeBuilder<AdvanceShippingNoticeLine> builder)
    {
        builder.ToTable("AdvanceShippingNoticeLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.ItemSkuSnapshot).IsRequired().HasMaxLength(50);
        builder.Property(line => line.ItemNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(line => line.EnteredUnitOfMeasure).IsRequired().HasMaxLength(20);
        builder.Property(line => line.ExpectedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.BaseUnitOfMeasure).IsRequired().HasMaxLength(20);
        builder.Property(line => line.ExpectedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ReceivedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionFactorToBase).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionPrecision).IsRequired();
        builder.Property(line => line.ConversionRoundingMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(line => line.ConversionRoundingDelta).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionPath).IsRequired().HasMaxLength(500);
        builder.Property(line => line.ConversionRuleIds).IsRequired().HasMaxLength(500);
        builder.Property(line => line.OverDeliveryTolerancePercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(line => line.UnderDeliveryTolerancePercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(line => line.ItemPackagingCodeSnapshot).HasMaxLength(40);
        builder.Property(line => line.ItemPackagingNameSnapshot).HasMaxLength(200);
        builder.Property(line => line.ItemPackagingUnitOfMeasureSnapshot).HasMaxLength(20);
        builder.Property(line => line.ItemPackagingUnitsPerPackageSnapshot).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PreAdvisedLotNumber).HasMaxLength(100);
        builder.Property(line => line.PreAdvisedExpiryDate).HasColumnType("date");
        builder.Property(line => line.PreAdvisedSerialNumber).HasMaxLength(100);
        builder.Property(line => line.ExpectedLicensePlateNumber).HasMaxLength(100);
        builder.Property(line => line.Notes).HasMaxLength(1_000);
        builder.Property(line => line.IsClosed).IsRequired();
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(line => line.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.ItemPackaging)
            .WithMany()
            .HasForeignKey(line => line.ItemPackagingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.PurchaseOrder)
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.AdvanceShippingNoticeId, line.LineNumber }).IsUnique();
        builder.HasIndex(line => new { line.PurchaseOrderLineId, line.AdvanceShippingNoticeId });
        builder.HasIndex(line => new { line.ItemId, line.AdvanceShippingNoticeId });
    }
}
