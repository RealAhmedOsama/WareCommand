using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts");
        builder.HasKey(receipt => receipt.Id);

        builder.Property(receipt => receipt.DocumentNumber).HasMaxLength(50).IsRequired();
        builder.Property(receipt => receipt.WarehouseCodeSnapshot).HasMaxLength(20).IsRequired();
        builder.Property(receipt => receipt.SupplierCodeSnapshot).HasMaxLength(50);
        builder.Property(receipt => receipt.SupplierNameSnapshot).HasMaxLength(200);
        builder.Property(receipt => receipt.SourceType).HasMaxLength(30).IsRequired();
        builder.Property(receipt => receipt.SourceReference).HasMaxLength(200);
        builder.Property(receipt => receipt.ExternalReference).HasMaxLength(100);
        builder.Property(receipt => receipt.SessionReference).HasMaxLength(100);
        builder.Property(receipt => receipt.Notes).HasMaxLength(2_000);
        builder.Property(receipt => receipt.Status).HasConversion<int>().IsRequired();
        builder.Property(receipt => receipt.CreatedByUserId).HasMaxLength(450);
        builder.Property(receipt => receipt.OpenedByUserId).HasMaxLength(450);
        builder.Property(receipt => receipt.CompletedByUserId).HasMaxLength(450);
        builder.Property(receipt => receipt.CancelledByUserId).HasMaxLength(450);
        builder.Property(receipt => receipt.ReversedByUserId).HasMaxLength(450);
        builder.Property(receipt => receipt.CorrectedByUserId).HasMaxLength(450);
        builder.Property(receipt => receipt.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(receipt => receipt.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.ReceivedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.OpenedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.ReversedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.CorrectedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(receipt => receipt.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(receipt => receipt.Warehouse)
            .WithMany()
            .HasForeignKey(receipt => receipt.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(receipt => receipt.Supplier)
            .WithMany()
            .HasForeignKey(receipt => receipt.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(receipt => receipt.PurchaseOrder)
            .WithMany()
            .HasForeignKey(receipt => receipt.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(receipt => receipt.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(receipt => receipt.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(receipt => receipt.DockLocation)
            .WithMany()
            .HasForeignKey(receipt => receipt.DockLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(receipt => receipt.ReceivingLocation)
            .WithMany()
            .HasForeignKey(receipt => receipt.ReceivingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(receipt => receipt.CorrectedReceipt)
            .WithMany()
            .HasForeignKey(receipt => receipt.CorrectedByReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(receipt => receipt.DocumentNumber).IsUnique();
        builder.HasIndex(receipt => new { receipt.WarehouseId, receipt.Status });
        builder.HasIndex(receipt => receipt.SupplierId);
        builder.HasIndex(receipt => receipt.PurchaseOrderId);
        builder.HasIndex(receipt => receipt.AdvanceShippingNoticeId);
        builder.HasIndex(receipt => receipt.ReceivedAtUtc);
        builder.HasIndex(receipt => new { receipt.SourceType, receipt.ExternalReference });
    }
}
