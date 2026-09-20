using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("PurchaseOrders");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.DocumentNumber)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(order => order.WarehouseCodeSnapshot)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(order => order.SupplierCodeSnapshot)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(order => order.SupplierNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(order => order.OrderDate)
            .HasColumnType("date")
            .IsRequired();
        builder.Property(order => order.ExpectedReceiptDate)
            .HasColumnType("date");
        builder.Property(order => order.ExternalReference)
            .HasMaxLength(100);
        builder.Property(order => order.SourceType)
            .IsRequired()
            .HasMaxLength(30);
        builder.Property(order => order.SourceReference)
            .HasMaxLength(200);
        builder.Property(order => order.CurrencyCodeSnapshot)
            .HasMaxLength(3);
        builder.Property(order => order.Notes)
            .HasMaxLength(2_000);
        builder.Property(order => order.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(order => order.CreatedByUserId)
            .HasMaxLength(450);
        builder.Property(order => order.ConfirmedByUserId)
            .HasMaxLength(450);
        builder.Property(order => order.CancelledByUserId)
            .HasMaxLength(450);
        builder.Property(order => order.ClosedByUserId)
            .HasMaxLength(450);
        builder.Property(order => order.ConfirmedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(order => order.CancelledAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(order => order.ClosedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(order => order.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(order => order.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(order => order.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(order => order.Warehouse)
            .WithMany()
            .HasForeignKey(order => order.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.Supplier)
            .WithMany()
            .HasForeignKey(order => order.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Lines)
            .WithOne(line => line.PurchaseOrder)
            .HasForeignKey(line => line.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(order => order.DocumentNumber).IsUnique();
        builder.HasIndex(order => new { order.SupplierId, order.SourceType, order.ExternalReference });
        builder.HasIndex(order => new { order.WarehouseId, order.Status, order.OrderDate });
        builder.HasIndex(order => new { order.SupplierId, order.OrderDate });
    }
}
