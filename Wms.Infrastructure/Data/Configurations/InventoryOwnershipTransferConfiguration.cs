using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InventoryOwnershipTransferConfiguration
    : IEntityTypeConfiguration<InventoryOwnershipTransfer>
{
    public void Configure(EntityTypeBuilder<InventoryOwnershipTransfer> builder)
    {
        builder.ToTable("InventoryOwnershipTransfers");
        builder.HasKey(transfer => transfer.Id);
        builder.Property(transfer => transfer.TransferNumber).HasMaxLength(80).IsRequired();
        builder.Property(transfer => transfer.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(transfer => transfer.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(transfer => transfer.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(transfer => transfer.SourceOwnerKind).HasConversion<int>().IsRequired();
        builder.Property(transfer => transfer.DestinationOwnerKind).HasConversion<int>().IsRequired();
        builder.Property(transfer => transfer.SourceOwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(transfer => transfer.DestinationOwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(transfer => transfer.Status).HasConversion<int>().IsRequired();
        builder.Property(transfer => transfer.SerialNumber).HasMaxLength(100);
        builder.Property(transfer => transfer.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(transfer => transfer.CompletedByUserId).HasMaxLength(450);
        builder.Property(transfer => transfer.CancelledByUserId).HasMaxLength(450);
        builder.Property(transfer => transfer.Reason).HasMaxLength(1_000);
        builder.Property(transfer => transfer.ExceptionReason).HasMaxLength(1_000);
        builder.Property(transfer => transfer.Quantity).HasColumnType("decimal(28,12)").IsRequired();
        builder.Property(transfer => transfer.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(transfer => transfer.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(transfer => transfer.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(transfer => transfer.Revision).IsRequired().IsConcurrencyToken();

        builder.HasOne(transfer => transfer.Warehouse)
            .WithMany()
            .HasForeignKey(transfer => transfer.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.Item)
            .WithMany()
            .HasForeignKey(transfer => transfer.ItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.Location)
            .WithMany()
            .HasForeignKey(transfer => transfer.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.InventoryStatus)
            .WithMany()
            .HasForeignKey(transfer => transfer.InventoryStatusId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.SourceOwner)
            .WithMany()
            .HasForeignKey(transfer => transfer.SourceInventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.DestinationOwner)
            .WithMany()
            .HasForeignKey(transfer => transfer.DestinationInventoryOwnerId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.Lot)
            .WithMany()
            .HasForeignKey(transfer => transfer.LotId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.Serial)
            .WithMany()
            .HasForeignKey(transfer => transfer.SerialNumberId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transfer => transfer.LicensePlate)
            .WithMany()
            .HasForeignKey(transfer => transfer.LicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(transfer => new { transfer.WarehouseId, transfer.TransferNumber }).IsUnique();
        builder.HasIndex(transfer => transfer.IdempotencyKey).IsUnique();
        builder.HasIndex(transfer => new { transfer.WarehouseId, transfer.ItemId, transfer.Status });
    }
}
