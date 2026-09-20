// Wms.Infrastructure/Data/Configurations/MovementConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.Data.Configurations;

public class MovementConfiguration : IEntityTypeConfiguration<Movement>
{
    public void Configure(EntityTypeBuilder<Movement> builder)
    {
        builder.ToTable("Movements");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.UserId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.SerialNumber)
            .HasMaxLength(100);

        builder.Property(e => e.SerialNumberId);

        builder.Property(e => e.InventoryStatusId)
            .IsRequired();

        builder.Property(e => e.LicensePlateId);
        builder.Property(e => e.FromLicensePlateId);
        builder.Property(e => e.ToLicensePlateId);

        builder.Property(e => e.AdjustmentBeforeQuantity)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.AdjustmentDelta)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.AdjustmentAfterQuantity)
            .HasColumnType("decimal(28,12)");

        builder.Property(e => e.FromInventoryStatusId);
        builder.Property(e => e.ToInventoryStatusId);
        builder.Property(e => e.StatusChangeLeg)
            .HasConversion<int>();

        builder.Property(e => e.ReferenceNumber)
            .HasMaxLength(100);

        builder.Property(e => e.Notes)
            .HasMaxLength(1000);

        builder.Property(e => e.Timestamp)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Value object configuration
        builder.Property(e => e.Quantity)
            .HasConversion(
                v => v.Value,
                v => new Quantity(v))
            .HasColumnType("decimal(28,12)")
            .IsRequired();

        builder.Property(e => e.EnteredQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(e => e.EnteredUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.BaseUnitOfMeasure)
            .IsRequired()
            .HasMaxLength(20);
        builder.Property(e => e.ConversionFactorToBase)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(e => e.ConversionPrecision)
            .IsRequired();
        builder.Property(e => e.ConversionRoundingMode)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(e => e.ConversionRoundingDelta)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(e => e.ConversionPath)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(e => e.ConversionRuleIds)
            .IsRequired()
            .HasMaxLength(500);
        builder.Property(e => e.PackagingCode)
            .HasMaxLength(40);
        builder.Property(e => e.PackagingName)
            .HasMaxLength(200);
        builder.Property(e => e.PackagingLocalizedName)
            .HasMaxLength(200);
        builder.Property(e => e.PackagingType)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(e => e.PackagingUnitOfMeasure)
            .HasMaxLength(20);
        builder.Property(e => e.PackagingPartialPackagePolicy)
            .HasConversion<string>()
            .HasMaxLength(30);
        builder.Property(e => e.PackagingUnitsPerPackage)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.PackagingGrossWeightKg)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.PackagingLengthCm)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.PackagingWidthCm)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.PackagingHeightCm)
            .HasColumnType("decimal(28,12)");
        builder.Property(e => e.PackagingVolumeCubicMeters)
            .HasColumnType("decimal(28,12)");

        // Relationships
        builder.HasOne(e => e.Item)
            .WithMany()
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FromLocation)
            .WithMany()
            .HasForeignKey(e => e.FromLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToLocation)
            .WithMany()
            .HasForeignKey(e => e.ToLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Lot)
            .WithMany()
            .HasForeignKey(e => e.LotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Serial)
            .WithMany()
            .HasForeignKey(e => e.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InventoryStatus)
            .WithMany()
            .HasForeignKey(e => e.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FromInventoryStatus)
            .WithMany()
            .HasForeignKey(e => e.FromInventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToInventoryStatus)
            .WithMany()
            .HasForeignKey(e => e.ToInventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.LicensePlate)
            .WithMany()
            .HasForeignKey(e => e.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.FromLicensePlate)
            .WithMany()
            .HasForeignKey(e => e.FromLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.ToLicensePlate)
            .WithMany()
            .HasForeignKey(e => e.ToLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.ItemId);
        builder.HasIndex(e => e.FromLocationId);
        builder.HasIndex(e => e.ToLocationId);
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Timestamp);
        builder.HasIndex(e => e.ReferenceNumber);
        builder.HasIndex(e => e.PackagingCode);
        builder.HasIndex(e => e.SerialNumberId);
        builder.HasIndex(e => e.InventoryStatusId);
        builder.HasIndex(e => e.LicensePlateId);
        builder.HasIndex(e => e.FromLicensePlateId);
        builder.HasIndex(e => e.ToLicensePlateId);
        builder.HasIndex(e => new { e.FromInventoryStatusId, e.ToInventoryStatusId });
    }
}
