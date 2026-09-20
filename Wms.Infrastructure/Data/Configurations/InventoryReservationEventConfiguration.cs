using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryReservationEventConfiguration
    : IEntityTypeConfiguration<InventoryReservationEvent>
{
    public void Configure(EntityTypeBuilder<InventoryReservationEvent> builder)
    {
        builder.ToTable("InventoryReservationEvents", table =>
            table.HasCheckConstraint(
                "CK_InventoryReservationEvents_QuantityNonNegative",
                "\"Quantity\" >= 0"));
        builder.HasKey(reservationEvent => reservationEvent.Id);
        builder.Property(reservationEvent => reservationEvent.Type)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(reservationEvent => reservationEvent.Quantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(reservationEvent => reservationEvent.ActorUserId)
            .HasMaxLength(450)
            .IsRequired();
        builder.Property(reservationEvent => reservationEvent.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reservationEvent => reservationEvent.CorrelationId)
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(reservationEvent => reservationEvent.Reason)
            .HasMaxLength(1_000);
        builder.Property(reservationEvent => reservationEvent.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(reservationEvent => reservationEvent.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(reservationEvent => reservationEvent.Reservation)
            .WithMany(reservation => reservation.Events)
            .HasForeignKey(reservationEvent => reservationEvent.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(reservationEvent => reservationEvent.Allocation)
            .WithMany()
            .HasForeignKey(reservationEvent => reservationEvent.AllocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(reservationEvent => new
        {
            reservationEvent.ReservationId,
            reservationEvent.OccurredAtUtc,
            reservationEvent.Id
        });
        builder.HasIndex(reservationEvent => reservationEvent.AllocationId);
    }
}
