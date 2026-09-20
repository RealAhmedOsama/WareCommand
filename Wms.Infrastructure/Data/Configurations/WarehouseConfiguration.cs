// Wms.Infrastructure/Data/Configurations/WarehouseConfiguration.cs

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.ArabicName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Address)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.ContactName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.ContactPhone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.ContactEmail)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(e => e.TimeZone)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.ExpiryWarningDays)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.Property(e => e.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Relationships
        builder.HasMany(e => e.Locations)
            .WithOne(l => l.Warehouse)
            .HasForeignKey(l => l.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.OperationalLocations)
            .WithOne(reference => reference.Warehouse)
            .HasForeignKey(reference => reference.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasIndex(e => e.IsActive);
    }
}
