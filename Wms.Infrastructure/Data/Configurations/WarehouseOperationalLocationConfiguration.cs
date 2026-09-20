using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseOperationalLocationConfiguration : IEntityTypeConfiguration<WarehouseOperationalLocation>
{
    public void Configure(EntityTypeBuilder<WarehouseOperationalLocation> builder)
    {
        builder.ToTable("WarehouseOperationalLocations");

        builder.HasKey(reference => reference.Id);

        builder.Property(reference => reference.Role)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(reference => reference.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(reference => reference.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(reference => reference.Warehouse)
            .WithMany(warehouse => warehouse.OperationalLocations)
            .HasForeignKey(reference => reference.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(reference => reference.Location)
            .WithMany()
            .HasForeignKey(reference => reference.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reference => new { reference.WarehouseId, reference.Role })
            .IsUnique();
        builder.HasIndex(reference => reference.LocationId);
    }
}
