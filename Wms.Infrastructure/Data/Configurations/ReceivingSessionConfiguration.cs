using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceivingSessionConfiguration : IEntityTypeConfiguration<ReceivingSession>
{
    public void Configure(EntityTypeBuilder<ReceivingSession> builder)
    {
        builder.ToTable("ReceivingSessions");
        builder.HasKey(session => session.Id);

        builder.Property(session => session.SourceType).HasConversion<int>().IsRequired();
        builder.Property(session => session.SessionReference).HasMaxLength(100).IsRequired();
        builder.Property(session => session.ExternalReference).HasMaxLength(100);
        builder.Property(session => session.Notes).HasMaxLength(2_000);
        builder.Property(session => session.Status).HasConversion<int>().IsRequired();
        builder.Property(session => session.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(session => session.SupervisorOverrideReason).HasMaxLength(1_000);
        builder.Property(session => session.CancelledByUserId).HasMaxLength(450);
        builder.Property(session => session.CancellationReason).HasMaxLength(1_000);
        builder.Property(session => session.OpenedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.PausedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(session => session.LastActivityAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(session => session.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(session => session.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(session => session.Warehouse)
            .WithMany()
            .HasForeignKey(session => session.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.ReceivingLocation)
            .WithMany()
            .HasForeignKey(session => session.ReceivingLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.PurchaseOrder)
            .WithMany()
            .HasForeignKey(session => session.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(session => session.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(session => session.DockLocation)
            .WithMany()
            .HasForeignKey(session => session.DockLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(session => new { session.WarehouseId, session.SessionReference }).IsUnique();
        builder.HasIndex(session => new { session.WarehouseId, session.Status, session.LastActivityAtUtc });
        builder.HasIndex(session => session.PurchaseOrderId);
        builder.HasIndex(session => session.AdvanceShippingNoticeId);
    }
}
