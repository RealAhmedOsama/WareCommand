using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.ReferenceType).HasMaxLength(80).IsRequired();
        builder.Property(attachment => attachment.ReferenceId).HasMaxLength(200).IsRequired();
        builder.Property(attachment => attachment.FileName).HasMaxLength(180).IsRequired();
        builder.Property(attachment => attachment.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(attachment => attachment.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(attachment => attachment.SizeBytes).IsRequired();
        builder.Property(attachment => attachment.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(attachment => attachment.UploaderUserId).HasMaxLength(450).IsRequired();
        builder.Property(attachment => attachment.Classification).HasConversion<int>().IsRequired();
        builder.Property(attachment => attachment.ScanStatus).HasConversion<int>().IsRequired();
        builder.Property(attachment => attachment.RetentionState).HasConversion<int>().IsRequired();
        builder.Property(attachment => attachment.UploadedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(attachment => attachment.RetentionUntilUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(attachment => attachment.LastScanMessage).HasMaxLength(1_000);
        builder.Property(attachment => attachment.DeletedByUserId).HasMaxLength(450);
        builder.Property(attachment => attachment.DeletedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(attachment => attachment.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(attachment => attachment.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne<Warehouse>()
            .WithMany()
            .HasForeignKey(attachment => attachment.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(attachment => new
        {
            attachment.ReferenceType,
            attachment.ReferenceId,
            attachment.WarehouseId,
            attachment.RetentionState
        });
        builder.HasIndex(attachment => new
        {
            attachment.ReferenceType,
            attachment.ReferenceId,
            attachment.WarehouseId,
            attachment.Sha256
        }).IsUnique();
        builder.HasIndex(attachment => new { attachment.ScanStatus, attachment.RetentionState });
    }
}
