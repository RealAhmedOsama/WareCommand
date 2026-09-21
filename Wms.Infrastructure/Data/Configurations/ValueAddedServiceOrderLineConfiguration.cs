using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class ValueAddedServiceOrderLineConfiguration : IEntityTypeConfiguration<ValueAddedServiceOrderLine>
{
    public void Configure(EntityTypeBuilder<ValueAddedServiceOrderLine> builder)
    {
        builder.ToTable("ValueAddedServiceOrderLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.Kind).HasConversion<int>().IsRequired();
        builder.Property(line => line.PlannedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ConsumedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ProducedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ScrapQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.ReversedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.SerialNumber).HasMaxLength(100);
        builder.Property(line => line.OwnerKind).HasConversion<int>().IsRequired();
        builder.Property(line => line.OwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(line => line.Notes).HasMaxLength(1_000);
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.SourceLocation)
            .WithMany()
            .HasForeignKey(line => line.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.DestinationLocation)
            .WithMany()
            .HasForeignKey(line => line.DestinationLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Lot)
            .WithMany()
            .HasForeignKey(line => line.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Serial)
            .WithMany()
            .HasForeignKey(line => line.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.LicensePlate)
            .WithMany()
            .HasForeignKey(line => line.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.InventoryStatus)
            .WithMany()
            .HasForeignKey(line => line.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.InventoryOwner)
            .WithMany()
            .HasForeignKey(line => line.InventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.KitDefinitionLine)
            .WithMany()
            .HasForeignKey(line => line.KitDefinitionLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Reservation)
            .WithMany()
            .HasForeignKey(line => line.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.ReservationAllocation)
            .WithMany()
            .HasForeignKey(line => line.ReservationAllocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.ValueAddedServiceOrderId, line.LineNumber }).IsUnique();
        builder.HasIndex(line => new { line.ItemId, line.Kind, line.LotId, line.SerialNumberId });
        builder.HasIndex(line => new { line.ReservationId, line.ReservationAllocationId });
        builder.HasIndex(line => new { line.OwnerKind, line.InventoryOwnerId });
    }
}
