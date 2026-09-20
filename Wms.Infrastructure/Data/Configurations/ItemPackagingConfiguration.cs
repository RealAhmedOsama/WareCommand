using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

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
            .HasColumnType("decimal(18,6)")
            .IsRequired();
        builder.Property(packaging => packaging.Barcode)
            .HasMaxLength(50);
        builder.Property(packaging => packaging.GrossWeightKg).HasColumnType("decimal(18,6)");
        builder.Property(packaging => packaging.LengthCm).HasColumnType("decimal(18,6)");
        builder.Property(packaging => packaging.WidthCm).HasColumnType("decimal(18,6)");
        builder.Property(packaging => packaging.HeightCm).HasColumnType("decimal(18,6)");
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
    }
}
