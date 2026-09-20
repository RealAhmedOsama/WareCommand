using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceivingSessionScanConfiguration : IEntityTypeConfiguration<ReceivingSessionScan>
{
    public void Configure(EntityTypeBuilder<ReceivingSessionScan> builder)
    {
        builder.ToTable("ReceivingSessionScans");
        builder.HasKey(scan => scan.Id);

        builder.Property(scan => scan.ClientOperationId).HasMaxLength(100).IsRequired();
        builder.Property(scan => scan.RawScanValue).HasMaxLength(200).IsRequired();
        builder.Property(scan => scan.ItemSku).HasMaxLength(50).IsRequired();
        builder.Property(scan => scan.EnteredQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(scan => scan.BaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(scan => scan.UnitOfMeasure).HasMaxLength(20);
        builder.Property(scan => scan.PackagingCode).HasMaxLength(40);
        builder.Property(scan => scan.LotNumber).HasMaxLength(100);
        builder.Property(scan => scan.ExpiryDate).HasColumnType("timestamp with time zone");
        builder.Property(scan => scan.SerialNumber).HasMaxLength(100);
        builder.Property(scan => scan.DestinationLocationCode).HasMaxLength(50);
        builder.Property(scan => scan.Notes).HasMaxLength(1_000);
        builder.Property(scan => scan.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(scan => scan.RequestedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(scan => scan.Status).HasConversion<int>().IsRequired();
        builder.Property(scan => scan.ReceiptDocumentNumber).HasMaxLength(50);
        builder.Property(scan => scan.IdempotencyKey).HasMaxLength(250);
        builder.Property(scan => scan.ErrorCode).HasMaxLength(200);
        builder.Property(scan => scan.ErrorMessage).HasMaxLength(2_000);
        builder.Property(scan => scan.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(scan => scan.CorrectedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(scan => scan.CorrectionReason).HasMaxLength(1_000);
        builder.Property(scan => scan.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(scan => scan.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(scan => scan.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(scan => scan.Session)
            .WithMany(session => session.Scans)
            .HasForeignKey(scan => scan.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(scan => scan.SessionLine)
            .WithMany(line => line.Scans)
            .HasForeignKey(scan => scan.ReceivingSessionLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(scan => scan.Movement)
            .WithMany()
            .HasForeignKey(scan => scan.MovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(scan => scan.Receipt)
            .WithMany()
            .HasForeignKey(scan => scan.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(scan => new { scan.ReceivingSessionId, scan.ClientOperationId }).IsUnique();
        builder.HasIndex(scan => new { scan.ReceivingSessionId, scan.Status });
        builder.HasIndex(scan => scan.MovementId);
        builder.HasIndex(scan => scan.ReceiptId);
        builder.HasIndex(scan => scan.RequestedAtUtc);
    }
}
