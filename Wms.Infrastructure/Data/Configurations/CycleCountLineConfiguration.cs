using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class CycleCountLineConfiguration : IEntityTypeConfiguration<CycleCountLine>
{
    public void Configure(EntityTypeBuilder<CycleCountLine> builder)
    {
        builder.ToTable("CycleCountLines", table => table.HasCheckConstraint(
            "CK_CycleCountLines_NonNegativeQuantities",
            "\"ExpectedQuantity\" >= 0 AND (\"CountedQuantity\" IS NULL OR \"CountedQuantity\" >= 0)"));
        builder.HasKey(line => line.Id);
        builder.Property(line => line.ExpectedQuantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(line => line.CountedQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.VarianceQuantity).HasColumnType("decimal(28,12)");
        builder.Property(line => line.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(line => line.SerialNumber).HasMaxLength(100);
        builder.Property(line => line.Status).HasConversion<int>().IsRequired();
        builder.Property(line => line.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(line => line.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(line => line.UpdatedAt).HasColumnType("timestamp with time zone");

        builder.HasOne(line => line.Task)
            .WithMany(task => task.Lines)
            .HasForeignKey(line => line.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(line => line.Warehouse)
            .WithMany()
            .HasForeignKey(line => line.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Location)
            .WithMany()
            .HasForeignKey(line => new { line.WarehouseId, line.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(line => line.Item)
            .WithMany()
            .HasForeignKey(line => line.ItemId)
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
        builder.HasIndex(line => new { line.TaskId, line.Sequence }).IsUnique();
        builder.HasIndex(line => new
        {
            line.WarehouseId,
            line.LocationId,
            line.ItemId,
            line.LotId,
            line.SerialNumberId,
            line.LicensePlateId,
            line.InventoryStatusId
        });
    }
}
