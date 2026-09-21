using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wms.Domain.Entities;

namespace Wms.Infrastructure.Data.Configurations;

public sealed class TransferOrderConfiguration : IEntityTypeConfiguration<TransferOrder>
{
    public void Configure(EntityTypeBuilder<TransferOrder> builder)
    {
        builder.ToTable("TransferOrders");
        builder.HasKey(value => value.Id);
        builder.Property(value => value.TransferNumber).HasMaxLength(80).IsRequired();
        builder.Property(value => value.CreationIdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(value => value.CreationRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(value => value.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(value => value.ExternalReference).HasMaxLength(100);
        builder.Property(value => value.Notes).HasMaxLength(2_000);
        builder.Property(value => value.CreatedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(value => value.CancelledByUserId).HasMaxLength(450);
        builder.Property(value => value.CancellationReason).HasMaxLength(1_000);
        builder.Property(value => value.ExceptionReason).HasMaxLength(1_000);
        builder.Property(value => value.CreatedAt).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(value => value.ConfirmedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ReleasedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ShippedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ReceivedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.ClosedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.CancelledAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(value => value.Revision).IsRequired().IsConcurrencyToken();
        builder.HasOne(value => value.SourceWarehouse).WithMany().HasForeignKey(value => value.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.DestinationWarehouse).WithMany().HasForeignKey(value => value.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(value => value.TransitLocation).WithMany().HasForeignKey(value => value.TransitLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => value.CreationIdempotencyKey).IsUnique();
        builder.HasIndex(value => new { value.SourceWarehouseId, value.TransferNumber }).IsUnique();
        builder.HasIndex(value => new { value.SourceWarehouseId, value.Status });
        builder.HasIndex(value => new { value.DestinationWarehouseId, value.Status });
    }
}
