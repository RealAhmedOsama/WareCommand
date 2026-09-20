using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ReceiptLineMovementConfiguration : IEntityTypeConfiguration<ReceiptLineMovement>
{
    public void Configure(EntityTypeBuilder<ReceiptLineMovement> builder)
    {
        builder.ToTable("ReceiptLineMovements");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.BaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.AcceptedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.RejectedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.DamagedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.QuarantinedBaseQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(link => link.Kind).HasConversion<int>().IsRequired();
        builder.Property(link => link.UserId).HasMaxLength(450).IsRequired();
        builder.Property(link => link.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(link => link.Reason).HasMaxLength(1_000);
        builder.Property(link => link.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(link => link.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(link => link.ReceiptLine)
            .WithMany(line => line.Movements)
            .HasForeignKey(link => link.ReceiptLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(link => link.Movement)
            .WithMany()
            .HasForeignKey(link => link.MovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(link => link.RelatedMovement)
            .WithMany()
            .HasForeignKey(link => link.RelatedMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(link => new { link.ReceiptLineId, link.MovementId }).IsUnique();
        builder.HasIndex(link => link.RelatedMovementId);
    }
}
