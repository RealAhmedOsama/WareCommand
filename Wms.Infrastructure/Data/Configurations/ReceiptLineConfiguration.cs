using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> builder)
    {
        builder.ToTable("ReceiptLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.ItemSkuSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(line => line.ItemNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(line => line.EnteredUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.ConversionPath).HasMaxLength(500).IsRequired();
        builder.Property(line => line.ConversionRuleIds).HasMaxLength(500).IsRequired();
        builder.Property(line => line.ConversionRoundingMode).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(line => line.PackagingCode).HasMaxLength(40);
        builder.Property(line => line.PackagingName).HasMaxLength(200);
        builder.Property(line => line.PackagingLocalizedName).HasMaxLength(200);
        builder.Property(line => line.PackagingType).HasConversion<string>().HasMaxLength(30);
        builder.Property(line => line.PackagingUnitOfMeasure).HasMaxLength(20);
        builder.Property(line => line.PackagingPartialPackagePolicy).HasConversion<string>().HasMaxLength(30);
        builder.Property(line => line.LotNumberSnapshot).HasMaxLength(100);
        builder.Property(line => line.SerialNumberSnapshot).HasMaxLength(100);
        builder.Property(line => line.LicensePlateNumberSnapshot).HasMaxLength(100);
        builder.Property(line => line.InventoryStatusCodeSnapshot).HasMaxLength(50);
        builder.Property(line => line.InventoryStatusNameSnapshot).HasMaxLength(200);
        builder.Property(line => line.Notes).HasMaxLength(1_000);
        builder.Property(line => line.OwnerKind).HasConversion<int>().IsRequired();
        builder.Property(line => line.OwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(line => line.ExpiryDateSnapshot).HasColumnType("timestamp with time zone");
        builder.Property(line => line.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();

        builder.Property(line => line.EnteredQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ExpectedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ReceivedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.AcceptedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.RejectedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.DamagedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.QuarantinedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionFactorToBase).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConversionRoundingDelta).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.PackagingUnitsPerPackage).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PackagingGrossWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PackagingLengthCm).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PackagingWidthCm).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PackagingHeightCm).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PackagingVolumeCubicMeters).HasColumnType("decimal(28,12)");

        builder.HasOne(line => line.Receipt)
            .WithMany(receipt => receipt.Lines)
            .HasForeignKey(line => line.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.PurchaseOrder)
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(line => line.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.AdvanceShippingNoticeLine)
            .WithMany()
            .HasForeignKey(line => line.AdvanceShippingNoticeLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.ReceivingLocation)
            .WithMany()
            .HasForeignKey(line => line.ReceivingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.InventoryStatus)
            .WithMany()
            .HasForeignKey(line => line.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.LicensePlate)
            .WithMany()
            .HasForeignKey(line => line.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.InventoryOwner)
            .WithMany()
            .HasForeignKey(line => line.InventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.ReceiptId, line.LineNumber }).IsUnique();
        builder.HasIndex(line => line.ItemId);
        builder.HasIndex(line => line.PurchaseOrderLineId);
        builder.HasIndex(line => line.AdvanceShippingNoticeLineId);
        builder.HasIndex(line => line.LicensePlateId);
        builder.HasIndex(line => line.InventoryStatusId);
        builder.HasIndex(line => new { line.OwnerKind, line.InventoryOwnerId });
    }
}
