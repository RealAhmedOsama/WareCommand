using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceivingSessionLineConfiguration : IEntityTypeConfiguration<ReceivingSessionLine>
{
    public void Configure(EntityTypeBuilder<ReceivingSessionLine> builder)
    {
        builder.ToTable("ReceivingSessionLines");
        builder.HasKey(line => line.Id);

        builder.Property(line => line.ItemSkuSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(line => line.ItemNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(line => line.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.ExpectedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PreviouslyReceivedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ReceivedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.OverDeliveryTolerancePercent).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(line => line.UnderDeliveryTolerancePercent).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(line => line.ExpectedLotNumber).HasMaxLength(100);
        builder.Property(line => line.ExpectedExpiryDate).HasColumnType("timestamp with time zone");
        builder.Property(line => line.ExpectedSerialNumber).HasMaxLength(100);
        builder.Property(line => line.ExpectedLicensePlateNumber).HasMaxLength(100);
        builder.Property(line => line.LastReceivedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(line => line.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(line => line.Session)
            .WithMany(session => session.Lines)
            .HasForeignKey(line => line.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(line => line.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.AdvanceShippingNoticeLine)
            .WithMany()
            .HasForeignKey(line => line.AdvanceShippingNoticeLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.ReceivingSessionId, line.LineNumber }).IsUnique();
        builder.HasIndex(line => line.ItemId);
        builder.HasIndex(line => line.PurchaseOrderLineId);
        builder.HasIndex(line => line.AdvanceShippingNoticeLineId);
    }
}
