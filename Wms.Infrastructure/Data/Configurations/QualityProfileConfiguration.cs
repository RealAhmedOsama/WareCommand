using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class QualityProfileConfiguration : IEntityTypeConfiguration<QualityProfile>
{
    public void Configure(EntityTypeBuilder<QualityProfile> builder)
    {
        builder.ToTable("QualityProfiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Code).HasMaxLength(50).IsRequired();
        builder.Property(profile => profile.Name).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.LocalizedName).HasMaxLength(200).IsRequired();
        builder.Property(profile => profile.ItemCategory).HasMaxLength(100);
        builder.Property(profile => profile.ReceiptSourceType).HasMaxLength(30);
        builder.Property(profile => profile.SamplingValue).HasColumnType("decimal(28,12)");
        builder.Property(profile => profile.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(profile => profile.Warehouse)
            .WithMany()
            .HasForeignKey(profile => profile.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(profile => profile.Supplier)
            .WithMany()
            .HasForeignKey(profile => profile.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(profile => profile.Item)
            .WithMany()
            .HasForeignKey(profile => profile.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(profile => profile.Code).IsUnique();
        builder.HasIndex(profile => new
        {
            profile.WarehouseId,
            profile.SupplierId,
            profile.ItemId,
            profile.ItemCategory,
            profile.ReceiptSourceType,
            profile.IsActive
        });
    }
}
