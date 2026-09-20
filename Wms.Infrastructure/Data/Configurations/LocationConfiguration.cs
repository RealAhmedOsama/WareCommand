// Wms.Infrastructure/Data/Configurations/LocationConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.Barcode)
            .HasMaxLength(100);

        builder.Property(e => e.StorageProfile)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.HazardClass)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.AccessRestriction)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.ConstraintAttributesJson)
            .HasMaxLength(4_000)
            .IsRequired();

        builder.Property(e => e.MaxUnits)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.MaxWeightKg)
            .HasColumnType("decimal(18,4)");

        builder.Property(e => e.MaxVolumeCubicMeters)
            .HasColumnType("decimal(18,6)");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Self-referencing relationship for hierarchy
        builder.HasAlternateKey(e => new { e.WarehouseId, e.Id });

        builder.HasOne(e => e.ParentLocation)
            .WithMany(e => e.ChildLocations)
            .HasForeignKey(e => new { e.WarehouseId, e.ParentLocationId })
            .HasPrincipalKey(e => new { e.WarehouseId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Warehouse relationship
        builder.HasOne(e => e.Warehouse)
            .WithMany(w => w.Locations)
            .HasForeignKey(e => e.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => new { e.WarehouseId, e.Code }).IsUnique();
        builder.HasIndex(e => e.WarehouseId);
        builder.HasIndex(e => e.ParentLocationId);
        builder.HasIndex(e => new { e.WarehouseId, e.Barcode }).IsUnique();
        builder.HasIndex(e => new { e.WarehouseId, e.Type, e.IsActive });
        builder.HasIndex(e => new { e.WarehouseId, e.ParentLocationId, e.Priority, e.Code });
        builder.HasIndex(e => new { e.IsActive, e.IsReceivable });
        builder.HasIndex(e => new { e.IsActive, e.IsPickable });
    }
}
