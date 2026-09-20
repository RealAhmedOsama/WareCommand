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

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Self-referencing relationship for hierarchy
        builder.HasOne(e => e.ParentLocation)
            .WithMany(e => e.ChildLocations)
            .HasForeignKey(e => e.ParentLocationId)
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
        builder.HasIndex(e => new { e.IsActive, e.IsReceivable });
        builder.HasIndex(e => new { e.IsActive, e.IsPickable });
    }
}
