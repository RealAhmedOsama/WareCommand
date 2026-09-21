using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class PickingPlanLineConfiguration : IEntityTypeConfiguration<PickingPlanLine>
{
    public void Configure(EntityTypeBuilder<PickingPlanLine> builder)
    {
        builder.ToTable("PickingPlanLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.OrderNumberSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(line => line.ItemSkuSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(line => line.PlannedQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.PickedQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.SerialNumber).HasMaxLength(100);
        builder.Property(line => line.UnitWeightKg).HasColumnType("decimal(28,12)");
        builder.Property(line => line.UnitVolumeCubicMeters).HasColumnType("decimal(28,12)");
        builder.Property(line => line.BatchKey).HasMaxLength(160);
        builder.Property(line => line.InventoryStatusId);
        builder.Property(line => line.Status).HasConversion<int>().IsRequired();
        builder.Property(line => line.LastError).HasMaxLength(2_000);
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(line => line.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(line => line.Plan)
            .WithMany(plan => plan.Lines)
            .HasForeignKey(line => line.PickingPlanId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(line => line.Work)
            .WithMany()
            .HasForeignKey(line => line.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.WorkLine)
            .WithMany()
            .HasForeignKey(line => line.WarehouseWorkLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.SalesOrder)
            .WithMany()
            .HasForeignKey(line => line.SalesOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.SalesOrderLine)
            .WithMany()
            .HasForeignKey(line => line.SalesOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.SourceLocation)
            .WithMany()
            .HasForeignKey(line => line.SourceLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.ZoneLocation)
            .WithMany()
            .HasForeignKey(line => line.ZoneLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Lot)
            .WithMany()
            .HasForeignKey(line => line.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Serial)
            .WithMany()
            .HasForeignKey(line => line.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.SourceLicensePlate)
            .WithMany()
            .HasForeignKey(line => line.SourceLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.InventoryStatus)
            .WithMany()
            .HasForeignKey(line => line.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Reservation)
            .WithMany()
            .HasForeignKey(line => line.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.ReservationAllocation)
            .WithMany()
            .HasForeignKey(line => line.ReservationAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Container)
            .WithMany(container => container.Lines)
            .HasForeignKey(line => line.PickingPlanContainerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Handoff)
            .WithMany()
            .HasForeignKey(line => line.PickingPlanHandoffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.PickingPlanId, line.Sequence }).IsUnique();
        builder.HasIndex(line => line.WarehouseWorkLineId).IsUnique();
        builder.HasIndex(line => new { line.PickingPlanId, line.SalesOrderId, line.ItemId, line.SourceLocationId });
    }
}
