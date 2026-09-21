using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("SalesOrders");
        builder.HasKey(order => order.Id);

        builder.Property(order => order.DocumentNumber).IsRequired().HasMaxLength(50);
        builder.Property(order => order.WarehouseCodeSnapshot).IsRequired().HasMaxLength(20);
        builder.Property(order => order.CustomerCodeSnapshot).IsRequired().HasMaxLength(50);
        builder.Property(order => order.CustomerLegalNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(order => order.CustomerLocalizedNameSnapshot).HasMaxLength(200);
        builder.Property(order => order.CustomerContactNameSnapshot).HasMaxLength(200);
        builder.Property(order => order.CustomerContactEmailSnapshot).HasMaxLength(320);
        builder.Property(order => order.CustomerContactPhoneSnapshot).HasMaxLength(50);
        builder.Property(order => order.ShipToCodeSnapshot).HasMaxLength(50);
        builder.Property(order => order.ShipToRecipientNameSnapshot).HasMaxLength(200);
        builder.Property(order => order.ShipToPhoneSnapshot).HasMaxLength(50);
        builder.Property(order => order.ShipToCountryCodeSnapshot).HasMaxLength(2);
        builder.Property(order => order.ShipToRegionSnapshot).HasMaxLength(100);
        builder.Property(order => order.ShipToCitySnapshot).HasMaxLength(100);
        builder.Property(order => order.ShipToPostalCodeSnapshot).HasMaxLength(30);
        builder.Property(order => order.ShipToAddressLine1Snapshot).HasMaxLength(200);
        builder.Property(order => order.ShipToAddressLine2Snapshot).HasMaxLength(200);
        builder.Property(order => order.ShipToDeliveryInstructionsSnapshot).HasMaxLength(1_000);
        builder.Property(order => order.OrderDate).IsRequired();
        builder.Property(order => order.RequestedShipDate);
        builder.Property(order => order.ExternalReference).HasMaxLength(100);
        builder.Property(order => order.SourceType).IsRequired().HasMaxLength(30);
        builder.Property(order => order.SourceReference).HasMaxLength(200);
        builder.Property(order => order.Priority).IsRequired();
        builder.Property(order => order.DefaultCarrierCodeSnapshot).HasMaxLength(50);
        builder.Property(order => order.DefaultCarrierServiceCodeSnapshot).HasMaxLength(80);
        builder.Property(order => order.PackagingProfileSnapshot).HasMaxLength(100);
        builder.Property(order => order.LabelProfileSnapshot).HasMaxLength(100);
        builder.Property(order => order.AllowPartialShipmentSnapshot).IsRequired();
        builder.Property(order => order.Notes).HasMaxLength(2_000);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(order => order.StatusBeforeHold).HasConversion<string>().HasMaxLength(30);
        builder.Property(order => order.HoldReason).HasMaxLength(1_000);
        builder.Property(order => order.CreatedByUserId).HasMaxLength(450);
        builder.Property(order => order.ConfirmedByUserId).HasMaxLength(450);
        builder.Property(order => order.HeldByUserId).HasMaxLength(450);
        builder.Property(order => order.CancelledByUserId).HasMaxLength(450);
        builder.Property(order => order.ClosedByUserId).HasMaxLength(450);
        builder.Property(order => order.ConfirmedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.HeldAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(order => order.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(order => order.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(order => order.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(order => order.Warehouse)
            .WithMany()
            .HasForeignKey(order => order.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(order => order.Customer)
            .WithMany()
            .HasForeignKey(order => order.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(order => order.Lines)
            .WithOne(line => line.SalesOrder)
            .HasForeignKey(line => line.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(order => order.DocumentNumber).IsUnique();
        builder.HasIndex(order => new { order.WarehouseId, order.SourceType, order.ExternalReference }).IsUnique();
        builder.HasIndex(order => new { order.WarehouseId, order.Status, order.Priority, order.RequestedShipDate });
        builder.HasIndex(order => new { order.CustomerId, order.OrderDate });
    }
}
