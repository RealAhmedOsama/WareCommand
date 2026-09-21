using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.ToTable("SupplierReturns");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ReturnNumber).HasMaxLength(80).IsRequired();
        builder.Property(value => value.SourceType).HasMaxLength(40).IsRequired();
        builder.Property(value => value.Reason).HasMaxLength(1_000).IsRequired();
        builder.Property(value => value.SupplierAuthorizationReference).HasMaxLength(120);
        builder.Property(value => value.ExternalReference).HasMaxLength(120);
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.ApprovedByUserId).HasMaxLength(450);
        builder.Property(value => value.ReleasedByUserId).HasMaxLength(450);
        builder.Property(value => value.PackedByUserId).HasMaxLength(450);
        builder.Property(value => value.CarrierCode).HasMaxLength(80);
        builder.Property(value => value.TrackingNumber).HasMaxLength(120);
        builder.Property(value => value.ShippingDocumentReference).HasMaxLength(120);
        builder.Property(value => value.ShippedByUserId).HasMaxLength(450);
        builder.Property(value => value.AcknowledgedByUserId).HasMaxLength(450);
        builder.Property(value => value.ClosedByUserId).HasMaxLength(450);
        builder.Property(value => value.CancelledByUserId).HasMaxLength(450);
        builder.Property(value => value.ExceptionReason).HasMaxLength(1_000);
        builder.Property(value => value.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.ApprovedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ReleasedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.PackedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ShippedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.AcknowledgedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(value => value.Warehouse)
            .WithMany()
            .HasForeignKey(value => value.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Supplier)
            .WithMany()
            .HasForeignKey(value => value.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.StagingLocation)
            .WithMany()
            .HasForeignKey(value => value.StagingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.PurchaseOrder)
            .WithMany()
            .HasForeignKey(value => value.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(value => value.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Receipt)
            .WithMany()
            .HasForeignKey(value => value.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.QualityInspection)
            .WithMany()
            .HasForeignKey(value => value.QualityInspectionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.WarehouseWork)
            .WithMany()
            .HasForeignKey(value => value.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(value => new { value.WarehouseId, value.ReturnNumber }).IsUnique();
        builder.HasIndex(value => new
        {
            value.WarehouseId,
            value.SupplierId,
            value.SupplierAuthorizationReference
        }).IsUnique();
        builder.HasIndex(value => new { value.WarehouseId, value.Status });
    }
}

public sealed class SupplierReturnLineConfiguration : IEntityTypeConfiguration<SupplierReturnLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnLine> builder)
    {
        builder.ToTable("SupplierReturnLines");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ItemSkuSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(value => value.ItemNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(value => value.RequestedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(value => value.ApprovedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(value => value.ReservedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(value => value.StagedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(value => value.ShippedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(value => value.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(value => value.SerialNumber).HasMaxLength(100);
        builder.Property(value => value.Reason).HasMaxLength(1_000).IsRequired();
        builder.Property(value => value.SourceReference).HasMaxLength(200);
        builder.Property(value => value.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(value => value.SupplierReturn)
            .WithMany(value => value.Lines)
            .HasForeignKey(value => value.SupplierReturnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.Warehouse)
            .WithMany()
            .HasForeignKey(value => value.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Item)
            .WithMany()
            .HasForeignKey(value => value.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SourceLocation)
            .WithMany()
            .HasForeignKey(value => value.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.InventoryStatus)
            .WithMany()
            .HasForeignKey(value => value.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Lot)
            .WithMany()
            .HasForeignKey(value => value.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Serial)
            .WithMany()
            .HasForeignKey(value => value.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.LicensePlate)
            .WithMany()
            .HasForeignKey(value => value.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(value => value.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.AdvanceShippingNoticeLine)
            .WithMany()
            .HasForeignKey(value => value.AdvanceShippingNoticeLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.ReceiptLine)
            .WithMany()
            .HasForeignKey(value => value.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.QualityInspection)
            .WithMany()
            .HasForeignKey(value => value.QualityInspectionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.QualityInspectionDisposition)
            .WithMany()
            .HasForeignKey(value => value.QualityInspectionDispositionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(value => new { value.SupplierReturnId, value.LineNumber }).IsUnique();
        builder.HasIndex(value => new
        {
            value.WarehouseId,
            value.ItemId,
            value.SourceLocationId,
            value.InventoryStatusId,
            value.LotId,
            value.SerialNumberId,
            value.LicensePlateId
        });
    }
}

public sealed class SupplierReturnCommandConfiguration : IEntityTypeConfiguration<SupplierReturnCommand>
{
    public void Configure(EntityTypeBuilder<SupplierReturnCommand> builder)
    {
        builder.ToTable("SupplierReturnCommands");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.Operation).HasMaxLength(100).IsRequired();
        builder.Property(value => value.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.UserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.ExecutedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.HasOne(value => value.SupplierReturn)
            .WithMany()
            .HasForeignKey(value => value.SupplierReturnId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(value => new { value.SupplierReturnId, value.Operation, value.IdempotencyKey }).IsUnique();
    }
}
