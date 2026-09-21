using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InboundExceptionConfiguration : IEntityTypeConfiguration<InboundException>
{
    public void Configure(EntityTypeBuilder<InboundException> builder)
    {
        builder.ToTable("InboundExceptions");
        builder.HasKey(exception => exception.Id);
        builder.Property(exception => exception.ExceptionNumber).IsRequired().HasMaxLength(80);
        builder.Property(exception => exception.IdempotencyKey).IsRequired().HasMaxLength(250);
        builder.Property(exception => exception.Code).HasConversion<int>().IsRequired();
        builder.Property(exception => exception.Severity).HasConversion<int>().IsRequired();
        builder.Property(exception => exception.Status).HasConversion<int>().IsRequired();
        builder.Property(exception => exception.Reason).IsRequired().HasMaxLength(2_000);
        builder.Property(exception => exception.QueueCode).IsRequired().HasMaxLength(50);
        builder.Property(exception => exception.ItemSkuSnapshot).HasMaxLength(50);
        builder.Property(exception => exception.Notes).HasMaxLength(2_000);
        builder.Property(exception => exception.AttachmentReferences).HasMaxLength(4_000);
        builder.Property(exception => exception.CreatedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(exception => exception.OwnerUserId).HasMaxLength(450);
        builder.Property(exception => exception.AssignedTeamCode).HasMaxLength(50);
        builder.Property(exception => exception.AssignedByUserId).HasMaxLength(450);
        builder.Property(exception => exception.ReviewStartedByUserId).HasMaxLength(450);
        builder.Property(exception => exception.Resolution).HasConversion<int?>();
        builder.Property(exception => exception.ResolutionReason).HasMaxLength(2_000);
        builder.Property(exception => exception.ResolvedByUserId).HasMaxLength(450);
        builder.Property(exception => exception.ExpectedBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(exception => exception.ActualBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(exception => exception.VarianceBaseQuantity).HasColumnType("decimal(28,12)");
        builder.Property(exception => exception.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(exception => exception.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(exception => exception.UpdatedAt).HasColumnType("timestamp with time zone");
        builder.Property(exception => exception.DueAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(exception => exception.AssignedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(exception => exception.ReviewStartedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(exception => exception.ResolvedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(exception => exception.Warehouse)
            .WithMany()
            .HasForeignKey(exception => exception.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.AdvanceShippingNotice)
            .WithMany()
            .HasForeignKey(exception => exception.AdvanceShippingNoticeId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.AdvanceShippingNoticeLine)
            .WithMany()
            .HasForeignKey(exception => exception.AdvanceShippingNoticeLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.Receipt)
            .WithMany()
            .HasForeignKey(exception => exception.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.ReceiptLine)
            .WithMany()
            .HasForeignKey(exception => exception.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.ReceivingSession)
            .WithMany()
            .HasForeignKey(exception => exception.ReceivingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.LicensePlate)
            .WithMany()
            .HasForeignKey(exception => exception.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.WarehouseWork)
            .WithMany()
            .HasForeignKey(exception => exception.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.Item)
            .WithMany()
            .HasForeignKey(exception => exception.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(exception => exception.StagingLocation)
            .WithMany()
            .HasForeignKey(exception => exception.StagingLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(exception => exception.ExceptionNumber).IsUnique();
        builder.HasIndex(exception => new { exception.WarehouseId, exception.IdempotencyKey }).IsUnique();
        builder.HasIndex(exception => new { exception.WarehouseId, exception.Status, exception.QueueCode, exception.Severity });
        builder.HasIndex(exception => new { exception.WarehouseId, exception.DueAtUtc, exception.Status });
        builder.HasIndex(exception => new { exception.ReceiptId, exception.ReceiptLineId });
        builder.HasIndex(exception => new { exception.ReceivingSessionId, exception.Status });
        builder.HasIndex(exception => exception.LicensePlateId);
    }
}
