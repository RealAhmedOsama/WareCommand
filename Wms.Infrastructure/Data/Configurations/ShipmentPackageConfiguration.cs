using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentPackageConfiguration : IEntityTypeConfiguration<ShipmentPackage>
{
    public void Configure(EntityTypeBuilder<ShipmentPackage> builder)
    {
        builder.ToTable("ShipmentPackages", table => table.HasCheckConstraint(
            "CK_ShipmentPackages_Tolerances",
            "\"ExpectedWeightKg\" IS NULL OR \"ExpectedWeightKg\" >= 0"));
        builder.HasKey(package => package.Id);
        builder.Property(package => package.PackageNumber).IsRequired().HasMaxLength(80);
        builder.Property(package => package.PackageType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(package => package.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(package => package.ExpectedWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(package => package.ActualWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(package => package.WeightToleranceKg).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(package => package.WeightTolerancePercent).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(package => package.LengthCm).HasColumnType("decimal(28,12)");
        builder.Property(package => package.WidthCm).HasColumnType("decimal(28,12)");
        builder.Property(package => package.HeightCm).HasColumnType("decimal(28,12)");
        builder.Property(package => package.VolumeCubicMeters).HasColumnType("decimal(28,12)");
        builder.Property(package => package.LabelReference).HasMaxLength(250);
        builder.Property(package => package.ClosedByUserId).HasMaxLength(450);
        builder.Property(package => package.ReopenReason).HasMaxLength(1_000);
        builder.Property(package => package.VoidReason).HasMaxLength(1_000);
        builder.Property(package => package.VoidedByUserId).HasMaxLength(450);
        builder.Property(package => package.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(package => package.VoidedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(package => package.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(package => package.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(package => package.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(package => package.Warehouse)
            .WithMany()
            .HasForeignKey(package => package.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(package => package.PackingSession)
            .WithMany(session => session.Packages)
            .HasForeignKey(package => package.PackingSessionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(package => package.TargetLicensePlate)
            .WithMany()
            .HasForeignKey(package => package.TargetLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(package => package.SalesOrder)
            .WithMany()
            .HasForeignKey(package => package.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(package => package.Contents)
            .WithOne(content => content.ShipmentPackage)
            .HasForeignKey(content => content.ShipmentPackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(package => new { package.WarehouseId, package.PackageNumber }).IsUnique();
        builder.HasIndex(package => package.TargetLicensePlateId).IsUnique();
        builder.HasIndex(package => new { package.PackingSessionId, package.Status });
        builder.HasIndex(package => new { package.SalesOrderId, package.Status });
    }
}
