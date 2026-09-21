using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class InternalMovementConfiguration : IEntityTypeConfiguration<InternalMovement>
{
    public void Configure(EntityTypeBuilder<InternalMovement> builder)
    {
        builder.ToTable("InternalMovements");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.Quantity).HasColumnType("numeric(28,12)").IsRequired();
        builder.Property(value => value.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(value => value.SerialNumber).HasMaxLength(100);
        builder.Property(value => value.OwnerKind).HasConversion<int>().IsRequired();
        builder.Property(value => value.OwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(value => value.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.Reason).HasMaxLength(1_000);
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.CancellationReason).HasMaxLength(1_000);
        builder.Property(value => value.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.CompletedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsRequired().IsConcurrencyToken();
        builder.HasOne(value => value.Warehouse).WithMany().HasForeignKey(value => value.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Item).WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SourceLocation).WithMany().HasForeignKey(value => value.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.DestinationLocation).WithMany().HasForeignKey(value => value.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Lot).WithMany().HasForeignKey(value => value.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Serial).WithMany().HasForeignKey(value => value.SerialNumberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.LicensePlate).WithMany().HasForeignKey(value => value.LicensePlateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.InventoryOwner).WithMany().HasForeignKey(value => value.InventoryOwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.WarehouseId, value.IdempotencyKey }).IsUnique();
        builder.HasIndex(value => new { value.WarehouseId, value.Status });
        builder.HasIndex(value => new { value.OwnerKind, value.InventoryOwnerId });
    }
}
