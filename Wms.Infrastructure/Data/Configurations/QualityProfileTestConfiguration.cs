using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class QualityProfileTestConfiguration : IEntityTypeConfiguration<QualityProfileTest>
{
    public void Configure(EntityTypeBuilder<QualityProfileTest> builder)
    {
        builder.ToTable("QualityProfileTests");
        builder.HasKey(test => test.Id);
        builder.Property(test => test.Code).HasMaxLength(50).IsRequired();
        builder.Property(test => test.Name).HasMaxLength(200).IsRequired();
        builder.Property(test => test.LocalizedName).HasMaxLength(200).IsRequired();
        builder.Property(test => test.MinimumValue).HasColumnType("decimal(28,12)");
        builder.Property(test => test.MaximumValue).HasColumnType("decimal(28,12)");
        builder.Property(test => test.AllowedValues).HasMaxLength(2_000);

        builder.HasOne(test => test.Profile)
            .WithMany(profile => profile.Tests)
            .HasForeignKey(test => test.QualityProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(test => new { test.QualityProfileId, test.Code }).IsUnique();
        builder.HasIndex(test => new { test.QualityProfileId, test.Sequence }).IsUnique();
    }
}
