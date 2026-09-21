using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;
using Wms.Domain.ValueObjects;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class TransferOrderLineConfiguration : IEntityTypeConfiguration<TransferOrderLine>
{
    public void Configure(EntityTypeBuilder<TransferOrderLine> builder)
    {
        builder.ToTable("TransferOrderLines");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.RequestedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.ShippedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.ReceivedQuantity).HasColumnType("numeric(28,12)");
        builder.Property(value => value.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
        builder.Property(value => value.SerialNumber).HasMaxLength(100);
        builder.Property(value => value.Notes).HasMaxLength(1_000);
        builder.Property(value => value.OwnerKind).HasConversion<int>().IsRequired();
        builder.Property(value => value.OwnerCodeSnapshot).HasMaxLength(80).IsRequired();
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.Revision).IsRequired().IsConcurrencyToken();
        builder.HasOne(value => value.TransferOrder).WithMany(value => value.Lines).HasForeignKey(value => value.TransferOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(value => value.Item).WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.SourceLocation).WithMany().HasForeignKey(value => value.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.DestinationLocation).WithMany().HasForeignKey(value => value.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Lot).WithMany().HasForeignKey(value => value.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.Serial).WithMany().HasForeignKey(value => value.SerialNumberId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.LicensePlate).WithMany().HasForeignKey(value => value.LicensePlateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.InventoryOwner).WithMany().HasForeignKey(value => value.InventoryOwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TransferOrderId, value.Sequence }).IsUnique();
        builder.HasIndex(value => new { value.ItemId, value.SourceLocationId, value.LotId, value.SerialNumberId, value.LicensePlateId });
        builder.HasIndex(value => new { value.OwnerKind, value.InventoryOwnerId });
    }
}
