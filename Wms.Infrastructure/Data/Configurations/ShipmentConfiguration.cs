using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.ShipmentNumber).HasMaxLength(80).IsRequired();
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.CarrierId);
        builder.Property(value => value.CarrierServiceId);
        builder.Property(value => value.ShipToRecipientName).HasMaxLength(200);
        builder.Property(value => value.ShipToPhone).HasMaxLength(50);
        builder.Property(value => value.ShipToCountryCode).HasMaxLength(2);
        builder.Property(value => value.ShipToRegion).HasMaxLength(100);
        builder.Property(value => value.ShipToCity).HasMaxLength(100);
        builder.Property(value => value.ShipToPostalCode).HasMaxLength(30);
        builder.Property(value => value.ShipToAddressLine1).HasMaxLength(200);
        builder.Property(value => value.ShipToAddressLine2).HasMaxLength(200);
        builder.Property(value => value.ShipToDeliveryInstructions).HasMaxLength(1000);
        builder.Property(value => value.ExternalReference).HasMaxLength(150);
        builder.Property(value => value.TrackingNumber).HasMaxLength(250);
        builder.Property(value => value.TrackingStatus).HasMaxLength(100);
        builder.Property(value => value.ExceptionReason).HasMaxLength(1000);
        builder.Property(value => value.CancelledByUserId).HasMaxLength(450);
        builder.Property(value => value.ClosedByUserId).HasMaxLength(450);
        builder.Property(value => value.CreatedByUserId).HasMaxLength(450);
        builder.Property(value => value.PlannedShipAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ActualShipAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.LastTrackingAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsConcurrencyToken();
        builder.HasOne(value => value.Warehouse).WithMany().HasForeignKey(value => value.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Carrier).WithMany().HasForeignKey(value => value.CarrierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.CarrierService).WithMany().HasForeignKey(value => value.CarrierServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.WarehouseId, value.ShipmentNumber }).IsUnique();
        builder.HasIndex(value => new { value.WarehouseId, value.Status });
        builder.HasIndex(value => value.ExternalReference);
    }
}
