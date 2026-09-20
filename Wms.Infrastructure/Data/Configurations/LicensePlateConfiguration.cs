using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class LicensePlateConfiguration : IEntityTypeConfiguration<LicensePlate>
{
    public void Configure(EntityTypeBuilder<LicensePlate> builder)
    {
        builder.ToTable("LicensePlates", table => table.HasCheckConstraint(
            "CK_LicensePlates_Dimensions",
            "(\"GrossWeightKg\" IS NULL OR \"GrossWeightKg\" >= 0) AND " +
            "(\"LengthCm\" IS NULL OR \"LengthCm\" >= 0) AND " +
            "(\"WidthCm\" IS NULL OR \"WidthCm\" >= 0) AND " +
            "(\"HeightCm\" IS NULL OR \"HeightCm\" >= 0)"));

        builder.HasKey(plate => plate.Id);
        builder.Property(plate => plate.Number)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(plate => plate.Type)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(plate => plate.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(plate => plate.IsSscc).IsRequired();
        builder.Property(plate => plate.IsActive).IsRequired();
        builder.Property(plate => plate.GrossWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(plate => plate.LengthCm).HasColumnType("decimal(28,12)");
        builder.Property(plate => plate.WidthCm).HasColumnType("decimal(28,12)");
        builder.Property(plate => plate.HeightCm).HasColumnType("decimal(28,12)");
        builder.Property(plate => plate.SourceReference).HasMaxLength(100);
        builder.Property(plate => plate.Notes).HasMaxLength(1_000);
        builder.Property(plate => plate.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(plate => plate.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
        builder.Property(plate => plate.Revision)
            .IsRequired()
            .IsConcurrencyToken();

        builder.HasOne(plate => plate.Warehouse)
            .WithMany()
            .HasForeignKey(plate => plate.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plate => plate.CurrentLocation)
            .WithMany()
            .HasForeignKey(plate => plate.CurrentLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(plate => plate.ParentLicensePlate)
            .WithMany(plate => plate.ChildLicensePlates)
            .HasForeignKey(plate => plate.ParentLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(plate => plate.Number).IsUnique();
        builder.HasIndex(plate => new { plate.WarehouseId, plate.CurrentLocationId });
        builder.HasIndex(plate => new { plate.WarehouseId, plate.Status });
        builder.HasIndex(plate => plate.ParentLicensePlateId);
    }
}
