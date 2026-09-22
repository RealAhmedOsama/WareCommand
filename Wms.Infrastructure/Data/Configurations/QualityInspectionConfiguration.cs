using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class QualityInspectionConfiguration : IEntityTypeConfiguration<QualityInspection>
{
    public void Configure(EntityTypeBuilder<QualityInspection> builder)
    {
        builder.ToTable("QualityInspections");
        builder.HasKey(inspection => inspection.Id);
        builder.Property(inspection => inspection.InspectionNumber).HasMaxLength(80).IsRequired();
        builder.Property(inspection => inspection.ItemSkuSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(inspection => inspection.ItemNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(inspection => inspection.ReceivedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(inspection => inspection.SampleBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(inspection => inspection.InspectedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(inspection => inspection.AcceptedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(inspection => inspection.RejectedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(inspection => inspection.SourceType).HasMaxLength(30);
        builder.Property(inspection => inspection.SupplierCodeSnapshot).HasMaxLength(50);
        builder.Property(inspection => inspection.QualityProfileCodeSnapshot).HasMaxLength(50);
        builder.Property(inspection => inspection.LotNumberSnapshot).HasMaxLength(100);
        builder.Property(inspection => inspection.SerialNumberSnapshot).HasMaxLength(100);
        builder.Property(inspection => inspection.LicensePlateNumberSnapshot).HasMaxLength(100);
        builder.Property(inspection => inspection.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(inspection => inspection.InspectorUserId).HasMaxLength(450);
        builder.Property(inspection => inspection.ClosedByUserId).HasMaxLength(450);
        builder.Property(inspection => inspection.ClosureReason).HasMaxLength(1_000);
        builder.Property(inspection => inspection.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(inspection => inspection.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(inspection => inspection.StartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(inspection => inspection.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(inspection => inspection.ExpiryDateSnapshot).HasColumnType("timestamp with time zone");

        builder.HasOne(inspection => inspection.Receipt)
            .WithMany()
            .HasForeignKey(inspection => inspection.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(inspection => inspection.ReceiptLine)
            .WithMany()
            .HasForeignKey(inspection => inspection.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(inspection => inspection.Warehouse)
            .WithMany()
            .HasForeignKey(inspection => inspection.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(inspection => inspection.Item)
            .WithMany()
            .HasForeignKey(inspection => inspection.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lot>()
            .WithMany()
            .HasForeignKey(inspection => inspection.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(inspection => inspection.Supplier)
            .WithMany()
            .HasForeignKey(inspection => inspection.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(inspection => inspection.QualityProfile)
            .WithMany()
            .HasForeignKey(inspection => inspection.QualityProfileId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(inspection => inspection.LicensePlate)
            .WithMany()
            .HasForeignKey(inspection => inspection.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(inspection => inspection.InspectionNumber).IsUnique();
        builder.HasIndex(inspection => inspection.ReceiptLineId)
            .IsUnique()
            .HasFilter("\"LicensePlateId\" IS NULL");
        builder.HasIndex(inspection => new { inspection.ReceiptLineId, inspection.LicensePlateId })
            .IsUnique()
            .HasFilter("\"LicensePlateId\" IS NOT NULL");
        builder.HasIndex(inspection => new { inspection.WarehouseId, inspection.Status, inspection.CreatedAt });
        builder.HasIndex(inspection => new { inspection.ItemId, inspection.Status });
    }
}
