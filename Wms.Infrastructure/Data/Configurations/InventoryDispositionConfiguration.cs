using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryDispositionConfiguration
    : IEntityTypeConfiguration<InventoryDisposition>
{
    public void Configure(EntityTypeBuilder<InventoryDisposition> builder)
    {
        builder.ToTable("InventoryDispositions");
        builder.HasKey(disposition => disposition.Id);
        builder.Property(disposition => disposition.DispositionNumber).HasMaxLength(80).IsRequired();
        builder.Property(disposition => disposition.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(disposition => disposition.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(disposition => disposition.Kind).HasConversion<int>().IsRequired();
        builder.Property(disposition => disposition.Status).HasConversion<int>().IsRequired();
        builder.Property(disposition => disposition.RequestedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(disposition => disposition.CompletedQuantity)
            .HasColumnType("decimal(28,12)")
            .IsRequired();
        builder.Property(disposition => disposition.Reason).HasMaxLength(1_000).IsRequired();
        builder.Property(disposition => disposition.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(disposition => disposition.ApprovedByUserId).HasMaxLength(450);
        builder.Property(disposition => disposition.CompletedByUserId).HasMaxLength(450);
        builder.Property(disposition => disposition.RejectionReason).HasMaxLength(1_000);
        builder.Property(disposition => disposition.FailureReason).HasMaxLength(1_000);
        builder.Property(disposition => disposition.ReferenceNumber).HasMaxLength(100);
        builder.Property(disposition => disposition.WitnessUserId).HasMaxLength(450);
        builder.Property(disposition => disposition.RequestedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(disposition => disposition.ApprovedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(disposition => disposition.CompletedAtUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(disposition => disposition.Revision).IsRequired().IsConcurrencyToken();
        builder.Property(disposition => disposition.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(disposition => disposition.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(disposition => disposition.Warehouse)
            .WithMany()
            .HasForeignKey(disposition => disposition.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.Stock)
            .WithMany()
            .HasForeignKey(disposition => disposition.StockId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.Item)
            .WithMany()
            .HasForeignKey(disposition => disposition.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.Location)
            .WithMany()
            .HasForeignKey(disposition => new { disposition.WarehouseId, disposition.LocationId })
            .HasPrincipalKey(location => new { location.WarehouseId, location.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.Lot)
            .WithMany()
            .HasForeignKey(disposition => disposition.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.Serial)
            .WithMany()
            .HasForeignKey(disposition => disposition.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.LicensePlate)
            .WithMany()
            .HasForeignKey(disposition => disposition.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.SourceInventoryStatus)
            .WithMany()
            .HasForeignKey(disposition => disposition.SourceInventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(disposition => disposition.TargetInventoryStatus)
            .WithMany()
            .HasForeignKey(disposition => disposition.TargetInventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(disposition => disposition.DispositionNumber).IsUnique();
        builder.HasIndex(disposition => new
        {
            disposition.WarehouseId,
            disposition.IdempotencyKey
        }).IsUnique();
        builder.HasIndex(disposition => new
        {
            disposition.WarehouseId,
            disposition.Status,
            disposition.Kind,
            disposition.RequestedAtUtc
        });
        builder.HasIndex(disposition => disposition.StockId);
        builder.HasIndex(disposition => disposition.LotId);
        builder.HasIndex(disposition => disposition.SerialNumberId);
        builder.HasIndex(disposition => disposition.LicensePlateId);
    }
}
