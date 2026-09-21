using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class QualityInspectionTestResultConfiguration : IEntityTypeConfiguration<QualityInspectionTestResult>
{
    public void Configure(EntityTypeBuilder<QualityInspectionTestResult> builder)
    {
        builder.ToTable("QualityInspectionTestResults");
        builder.HasKey(result => result.Id);
        builder.Property(result => result.TestCodeSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(result => result.TestNameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(result => result.InspectedQuantity).HasColumnType("decimal(28,12)");
        builder.Property(result => result.NumericValue).HasColumnType("decimal(28,12)");
        builder.Property(result => result.RecordedValue).HasMaxLength(500);
        builder.Property(result => result.Notes).HasMaxLength(1_000);
        builder.Property(result => result.AttachmentReferences).HasMaxLength(2_000);
        builder.Property(result => result.RecordedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(result => result.RecordedAtUtc).HasColumnType("timestamp with time zone");

        builder.HasOne(result => result.Inspection)
            .WithMany(inspection => inspection.Results)
            .HasForeignKey(result => result.QualityInspectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(result => result.ProfileTest)
            .WithMany()
            .HasForeignKey(result => result.QualityProfileTestId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(result => new { result.QualityInspectionId, result.QualityProfileTestId }).IsUnique();
    }
}
