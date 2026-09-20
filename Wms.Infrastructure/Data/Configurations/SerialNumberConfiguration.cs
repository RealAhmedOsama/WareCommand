using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
{
    public void Configure(EntityTypeBuilder<SerialNumber> builder)
    {
        builder.ToTable("SerialNumbers");

        builder.HasKey(serial => serial.Id);

        builder.Property(serial => serial.Number)
            .IsRequired()
            .HasMaxLength(100);
        builder.Property(serial => serial.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(serial => serial.CurrentLicensePlate)
            .HasMaxLength(100);
        builder.Property(serial => serial.StatusReason)
            .HasMaxLength(1_000);
        builder.Property(serial => serial.ReceiptReference)
            .HasMaxLength(100);
        builder.Property(serial => serial.ShipmentReference)
            .HasMaxLength(100);
        builder.Property(serial => serial.ConflictReason)
            .HasMaxLength(1_000);
        builder.Property(serial => serial.LastMovedAt)
            .HasColumnType("timestamp with time zone");
        builder.Property(serial => serial.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(serial => serial.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(serial => serial.Item)
            .WithMany()
            .HasForeignKey(serial => serial.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(serial => serial.Lot)
            .WithMany()
            .HasForeignKey(serial => serial.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(serial => serial.CurrentWarehouse)
            .WithMany()
            .HasForeignKey(serial => serial.CurrentWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(serial => serial.CurrentLocation)
            .WithMany()
            .HasForeignKey(serial => serial.CurrentLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(serial => new { serial.ItemId, serial.Number })
            .IsUnique();
        builder.HasIndex(serial => serial.CurrentLocationId);
        builder.HasIndex(serial => serial.CurrentWarehouseId);
        builder.HasIndex(serial => serial.Status);
        builder.HasIndex(serial => serial.HasMigrationConflict);
    }
}
