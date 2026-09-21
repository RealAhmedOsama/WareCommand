using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class WarehouseWorkLineConfiguration : IEntityTypeConfiguration<WarehouseWorkLine>
{
    public void Configure(EntityTypeBuilder<WarehouseWorkLine> builder)
    {
        builder.ToTable("WarehouseWorkLines");
        builder.HasKey(line => line.Id);
        builder.Property(line => line.PlannedQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.ActualQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.SerialNumber).HasMaxLength(100);
        builder.Property(line => line.SourceReference).HasMaxLength(200);
        builder.Property(line => line.DimensionsSnapshot).HasMaxLength(2_000);
        builder.Property(line => line.ReservationId);
        builder.Property(line => line.ReservationAllocationId);
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(line => line.Work)
            .WithMany(work => work.Lines)
            .HasForeignKey(line => line.WarehouseWorkId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(line => line.Warehouse)
            .WithMany()
            .HasForeignKey(line => line.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
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

        builder.HasIndex(line => new { line.WarehouseWorkId, line.Sequence }).IsUnique();
        builder.HasIndex(line => new { line.ItemId, line.SourceLocationId, line.LicensePlateId });
        builder.HasIndex(line => new { line.ReservationId, line.ReservationAllocationId });
    }
}
