using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryReservationAllocationConfiguration
    : IEntityTypeConfiguration<InventoryReservationAllocation>
{
    public void Configure(EntityTypeBuilder<InventoryReservationAllocation> builder)
    {
        builder.ToTable("InventoryReservationAllocations", table =>
            table.HasCheckConstraint(
                "CK_InventoryReservationAllocations_QuantitiesValid",
                "\"AllocatedQuantity\" > 0 AND \"ConsumedQuantity\" >= 0 AND \"ReleasedQuantity\" >= 0 AND \"ConsumedQuantity\" + \"ReleasedQuantity\" <= \"AllocatedQuantity\""));
        builder.HasKey(allocation => allocation.Id);
        builder.Property(allocation => allocation.SerialNumber)
            .HasMaxLength(100);
        builder.Property(allocation => allocation.BaseUnitOfMeasure)
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(allocation => allocation.OwnerKind)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(allocation => allocation.OwnerCodeSnapshot)
            .HasMaxLength(80)
            .IsRequired();
        builder.Property(allocation => allocation.AllocatedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.ConsumedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.ReleasedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(allocation => allocation.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(allocation => allocation.Reason)
            .HasMaxLength(1_000);
        builder.Property(allocation => allocation.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(allocation => allocation.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(allocation => allocation.UpdatedAt)
            .HasColumnType("timestamp with time zone");
        builder.Ignore(allocation => allocation.RemainingQuantity);

        builder.HasOne(allocation => allocation.Reservation)
            .WithMany(reservation => reservation.Allocations)
            .HasForeignKey(allocation => allocation.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.Warehouse)
            .WithMany()
            .HasForeignKey(allocation => allocation.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.Location)
            .WithMany()
            .HasForeignKey(allocation => new { allocation.WarehouseId, allocation.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.Item)
            .WithMany()
            .HasForeignKey(allocation => allocation.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.Lot)
            .WithMany()
            .HasForeignKey(allocation => allocation.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.SerialNumberEntity)
            .WithMany()
            .HasForeignKey(allocation => allocation.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.LicensePlate)
            .WithMany()
            .HasForeignKey(allocation => allocation.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.InventoryStatus)
            .WithMany()
            .HasForeignKey(allocation => allocation.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(allocation => allocation.InventoryOwner)
            .WithMany()
            .HasForeignKey(allocation => allocation.InventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(allocation => new
        {
            allocation.ReservationId,
            allocation.Status
        });
        builder.HasIndex(allocation => new
        {
            allocation.WarehouseId,
            allocation.ItemId,
            allocation.LocationId
        });
        builder.HasIndex(allocation => allocation.LotId);
        builder.HasIndex(allocation => allocation.SerialNumberId);
        builder.HasIndex(allocation => allocation.LicensePlateId);
        builder.HasIndex(allocation => allocation.InventoryOwnerId);
    }
}
