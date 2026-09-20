using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PurchaseOrderReceiptAllocationConfiguration
    : IEntityTypeConfiguration<PurchaseOrderReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderReceiptAllocation> builder)
    {
        builder.ToTable("PurchaseOrderReceiptAllocations");
        builder.HasKey(allocation => allocation.Id);

        builder.Property(allocation => allocation.ReceivedBaseQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.EnteredQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.EnteredUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(allocation => allocation.BaseUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(allocation => allocation.ConversionFactorToBase)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.ConversionPrecision).IsRequired();
        builder.Property(allocation => allocation.ConversionRoundingMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(allocation => allocation.ConversionRoundingDelta)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.ConversionPath)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(allocation => allocation.ConversionRuleIds)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(allocation => allocation.ReferenceNumber)
            .HasMaxLength(100);
        builder.Property(allocation => allocation.ReceivedByUserId)
            .IsRequired()
            .HasMaxLength(450);
        builder.Property(allocation => allocation.ReceivedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(allocation => allocation.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(allocation => allocation.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(allocation => allocation.PurchaseOrder)
            .WithMany()
            .HasForeignKey(allocation => allocation.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(allocation => allocation.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.Movement)
            .WithMany()
            .HasForeignKey(allocation => allocation.MovementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(allocation => allocation.PurchaseOrderId);
        builder.HasIndex(allocation => allocation.PurchaseOrderLineId);
        builder.HasIndex(allocation => allocation.MovementId).IsUnique();
        builder.HasIndex(allocation => new { allocation.PurchaseOrderLineId, allocation.ReceivedAtUtc });
    }
}
