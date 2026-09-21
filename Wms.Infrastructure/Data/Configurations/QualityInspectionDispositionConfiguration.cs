using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class QualityInspectionDispositionConfiguration : IEntityTypeConfiguration<QualityInspectionDisposition>
{
    public void Configure(EntityTypeBuilder<QualityInspectionDisposition> builder)
    {
        builder.ToTable("QualityInspectionDispositions");
        builder.HasKey(disposition => disposition.Id);
        builder.Property(disposition => disposition.Quantity).HasColumnType("decimal(28,12)");
        builder.Property(disposition => disposition.Reason).HasMaxLength(1_000).IsRequired();
        builder.Property(disposition => disposition.RecordedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(disposition => disposition.RecordedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(disposition => disposition.ReferenceNumber).HasMaxLength(100);
        builder.Property(disposition => disposition.OverrideReason).HasMaxLength(1_000);

        builder.HasOne(disposition => disposition.Inspection)
            .WithMany(inspection => inspection.Dispositions)
            .HasForeignKey(disposition => disposition.QualityInspectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(disposition => disposition.Movement)
            .WithMany()
            .HasForeignKey(disposition => disposition.MovementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.TargetLocation)
            .WithMany()
            .HasForeignKey(disposition => disposition.TargetLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(disposition => new { disposition.QualityInspectionId, disposition.RecordedAtUtc });
        builder.HasIndex(disposition => disposition.MovementId);
    }
}
