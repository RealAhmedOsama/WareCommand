using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryReservationConfiguration
    : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", table =>
            table.HasCheckConstraint(
                "CK_InventoryReservations_RequestedPositive",
                "\"RequestedQuantity\" > 0"));
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.DemandType)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(reservation => reservation.DemandId)
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(reservation => reservation.DemandKey)
            .HasMaxLength(350)
            .IsRequired();
        builder.Property(reservation => reservation.RequestedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(reservation => reservation.Mode)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(reservation => reservation.Status)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(reservation => reservation.StatusReason)
            .HasMaxLength(1_000);
        builder.Property(reservation => reservation.ExpiresAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(reservation => reservation.SelectorSerialNumber)
            .HasMaxLength(100);
        builder.Property(reservation => reservation.SelectorBaseUnitOfMeasure)
            .HasMaxLength(20);
        builder.Property(reservation => reservation.SelectorOwnerKind)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(reservation => reservation.SelectorOwnerCodeSnapshot)
            .HasMaxLength(80);
        builder.Property(reservation => reservation.AllocationStrategyKey)
            .HasMaxLength(80);
        builder.Property(reservation => reservation.AllocationStrategy)
            .HasConversion<int>();
        builder.Property(reservation => reservation.AllocationStrategyMissingExpiryFallback)
            .HasConversion<int>();
        builder.Property(reservation => reservation.AllocationStrategyMinimumShelfLifeDays)
            .IsRequired();
        builder.Property(reservation => reservation.AllocationStrategyPreferWholeLicensePlate)
            .IsRequired();
        builder.Property(reservation => reservation.ActorUserId)
            .HasMaxLength(450)
            .IsRequired();
        builder.Property(reservation => reservation.CorrelationId)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(reservation => reservation.Revision)
            .IsRequired()
            .IsConcurrencyToken();
        builder.Property(reservation => reservation.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reservation => reservation.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(reservation => reservation.Warehouse)
            .WithMany()
            .HasForeignKey(reservation => reservation.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(reservation => reservation.Item)
            .WithMany()
            .HasForeignKey(reservation => reservation.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reservation => new
        {
            reservation.WarehouseId,
            reservation.DemandKey
        }).IsUnique();
        builder.HasIndex(reservation => new
        {
            reservation.WarehouseId,
            reservation.ItemId,
            reservation.Status,
            reservation.ExpiresAtUtc
        });
        builder.HasIndex(reservation => new
        {
            reservation.DemandType,
            reservation.DemandId,
            reservation.DemandLine
        });
    }
}
