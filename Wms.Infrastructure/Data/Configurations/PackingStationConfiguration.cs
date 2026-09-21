using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PackingStationConfiguration : IEntityTypeConfiguration<PackingStation>
{
    public void Configure(EntityTypeBuilder<PackingStation> builder)
    {
        builder.ToTable("PackingStations");
        builder.HasKey(station => station.Id);
        builder.Property(station => station.Code).IsRequired().HasMaxLength(50);
        builder.Property(station => station.Name).IsRequired().HasMaxLength(200);
        builder.Property(station => station.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(station => station.SupportedDevices).HasMaxLength(2_000);
        builder.Property(station => station.PrinterProfile).HasMaxLength(200);
        builder.Property(station => station.ScaleProfile).HasMaxLength(200);
        builder.Property(station => station.AllowedUserIds).HasMaxLength(4_000);
        builder.Property(station => station.PermissionProfile).HasMaxLength(100);
        builder.Property(station => station.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(station => station.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(station => station.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(station => station.Warehouse)
            .WithMany()
            .HasForeignKey(station => station.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(station => station.Location)
            .WithMany()
            .HasForeignKey(station => station.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(station => new { station.WarehouseId, station.Code }).IsUnique();
        builder.HasIndex(station => new { station.WarehouseId, station.Status });
    }
}
