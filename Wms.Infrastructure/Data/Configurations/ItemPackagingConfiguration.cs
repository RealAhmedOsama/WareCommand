using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ItemPackagingConfiguration : IEntityTypeConfiguration<ItemPackaging>
{
    public void Configure(EntityTypeBuilder<ItemPackaging> builder)
    {
        builder.ToTable("ItemPackagings");
        builder.HasKey(packaging => packaging.Id);

        builder.Property(packaging => packaging.Code)
            .IsRequired()
            .HasMaxLength(40);
        builder.Property(packaging => packaging.UnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(packaging => packaging.UnitsPerPackage)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(packaging => packaging.Name)
            .IsRequired()
            .HasMaxLength(200)
            .HasDefaultValue(string.Empty);
        builder.Property(packaging => packaging.LocalizedName)
            .IsRequired()
            .HasMaxLength(200)
            .HasDefaultValue(string.Empty);
        builder.Property(packaging => packaging.Barcode)
            .HasMaxLength(50);
        builder.Property(packaging => packaging.Gtin)
            .HasMaxLength(14);
        builder.Property(packaging => packaging.ParentPackagingCode)
            .HasMaxLength(40);
        builder.Property(packaging => packaging.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(PackagingType.Other)
            .IsRequired();
        builder.Property(packaging => packaging.PartialPackagePolicy)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(PackagingPartialPolicy.Reject)
            .IsRequired();
        builder.Property(packaging => packaging.GrossWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(packaging => packaging.LengthCm).HasColumnType("decimal(28,12)");
        builder.Property(packaging => packaging.WidthCm).HasColumnType("decimal(28,12)");
        builder.Property(packaging => packaging.HeightCm).HasColumnType("decimal(28,12)");
        builder.Property(packaging => packaging.VolumeCubicMeters).HasColumnType("decimal(28,12)");
        builder.Property(packaging => packaging.IsDefault).IsRequired();
        builder.Property(packaging => packaging.IsDefaultReceiving).IsRequired();
        builder.Property(packaging => packaging.IsDefaultStorage).IsRequired();
        builder.Property(packaging => packaging.IsDefaultPicking).IsRequired();
        builder.Property(packaging => packaging.IsDefaultShipping).IsRequired();
        builder.Property(packaging => packaging.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
        builder.Property(packaging => packaging.Version)
            .IsRequired()
            .HasDefaultValue(1);
        builder.Property(packaging => packaging.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(packaging => packaging.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(packaging => packaging.Item)
            .WithMany(item => item.Packagings)
            .HasForeignKey(packaging => packaging.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(packaging => new { packaging.ItemId, packaging.Code }).IsUnique();
        builder.HasIndex(packaging => packaging.Barcode).IsUnique();
        builder.HasIndex(packaging => packaging.Gtin).IsUnique();
        builder.HasIndex(packaging => new { packaging.ItemId, packaging.ParentPackagingCode });
        builder.HasIndex(packaging => new { packaging.ItemId, packaging.IsActive });
    }
}
