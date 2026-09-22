using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class AdvanceShippingNoticeConfiguration : IEntityTypeConfiguration<AdvanceShippingNotice>
{
    public void Configure(EntityTypeBuilder<AdvanceShippingNotice> builder)
    {
        builder.ToTable("AdvanceShippingNotices");
        builder.HasKey(notice => notice.Id);

        builder.Property(notice => notice.DocumentNumber)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(notice => notice.WarehouseCodeSnapshot)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(notice => notice.SupplierCodeSnapshot)
            .IsRequired()
            .HasMaxLength(50);
        builder.Property(notice => notice.SupplierNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);
        builder.Property(notice => notice.CarrierName).HasMaxLength(200);
        builder.Property(notice => notice.ExpectedArrivalFromUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.ExpectedArrivalToUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.VehicleNumber).HasMaxLength(100);
        builder.Property(notice => notice.TrailerNumber).HasMaxLength(100);
        builder.Property(notice => notice.ContainerNumber).HasMaxLength(100);
        builder.Property(notice => notice.TrackingReference).HasMaxLength(100);
        builder.Property(notice => notice.ExternalReference).HasMaxLength(100);
        builder.Property(notice => notice.SourceType)
            .IsRequired()
            .HasMaxLength(30);
        builder.Property(notice => notice.SourceReference).HasMaxLength(200);
        builder.Property(notice => notice.SourcePayload).HasMaxLength(8_000);
        builder.Property(notice => notice.Notes).HasMaxLength(2_000);
        builder.Property(notice => notice.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(notice => notice.CreatedByUserId).HasMaxLength(450);
        builder.Property(notice => notice.SubmittedByUserId).HasMaxLength(450);
        builder.Property(notice => notice.ArrivedByUserId).HasMaxLength(450);
        builder.Property(notice => notice.CompletedByUserId).HasMaxLength(450);
        builder.Property(notice => notice.CancelledByUserId).HasMaxLength(450);
        builder.Property(notice => notice.SubmittedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.ArrivedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(notice => notice.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(notice => notice.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(notice => notice.Warehouse)
            .WithMany()
            .HasForeignKey(notice => notice.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(notice => notice.Supplier)
            .WithMany()
            .HasForeignKey(notice => notice.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(notice => notice.DockLocation)
            .WithMany()
            .HasForeignKey(notice => notice.DockLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(notice => notice.Lines)
            .WithOne(line => line.AdvanceShippingNotice)
            .HasForeignKey(line => line.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(notice => notice.DocumentNumber).IsUnique();
        builder.HasIndex(notice => new { notice.SupplierId, notice.SourceType, notice.ExternalReference })
            .IsUnique()
            .HasFilter("\"ExternalReference\" IS NOT NULL");
        builder.HasIndex(notice => new { notice.WarehouseId, notice.Status, notice.ExpectedArrivalFromUtc });
        builder.HasIndex(notice => new { notice.SupplierId, notice.ExpectedArrivalFromUtc });
    }
}
